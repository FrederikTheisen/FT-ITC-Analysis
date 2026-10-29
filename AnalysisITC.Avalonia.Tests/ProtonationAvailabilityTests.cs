using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProtonationAvailabilityTests
{
    [AvaloniaFact]
    public async Task MissingThermochemistryExplainsUnavailableRowWithoutInspectorParagraphs()
    {
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        var result = CreateResult(Buffer.MES, Buffer.Hepes, Buffer.Succinate);
        var longName = "Succinate experiment with a deliberately long descriptive name covering several solution conditions and sample identifiers";
        result.Solution.Solutions[2].Data.Name = longName;
        result = new AnalysisResult(result.Solution);
        var workspace = new AnalysisResultWorkspaceControl { Result = result };
        Assert.DoesNotContain(ResultAnalysisViewMode.Protonation, workspace.AvailableViewModes);
        var section = (Control)typeof(AnalysisResultWorkspaceControl)
            .GetMethod("BuildAvailabilitySection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, null)!;
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var window = new Window { Width = 320, Height = 500, Content = section };
        window.Show();
        try
        {
            window.UpdateLayout();
            var labels = section.GetLogicalDescendants().OfType<TextBlock>().ToList();
            var heading = labels.Single(label => label.Text == "Protonation");
            var row = (Grid)heading.Parent!;
            var status = row.Children.OfType<TextBlock>().Last();
            Assert.Equal("Unavailable", status.Text);
            Assert.Equal(result.ProtonationAnalysisUnavailableReason, ToolTip.GetTip(status));
            Assert.Equal(result.ProtonationAnalysisUnavailableReason, ToolTip.GetTip(heading));
            Assert.Equal(result.ProtonationAnalysisUnavailableReason, ToolTip.GetTip((Control)row.Parent!));
            Assert.Contains(longName, (string)ToolTip.GetTip(status)!);
            foreach (var entry in new[]
            {
                ("Temperature presentation", result.TemperatureDependenceUnavailableReason),
                ("Structuring", result.SpolarRecordAnalysisUnavailableReason),
                ("Electrostatics", result.ElectrostaticsAnalysisUnavailableReason)
            })
            {
                var label = labels.Single(value => value.Text == entry.Item1);
                var value = ((Grid)label.Parent!).Children.OfType<TextBlock>().Last();
                Assert.Equal("Unavailable", value.Text);
                Assert.False(string.IsNullOrWhiteSpace(entry.Item2));
                Assert.Equal(entry.Item2, ToolTip.GetTip(label));
                Assert.Equal(entry.Item2, ToolTip.GetTip(value));
            }
            Assert.DoesNotContain(labels, label => label.Text == result.ProtonationAnalysisUnavailableReason);
            Assert.DoesNotContain(labels, label => label.Text == result.ElectrostaticsAnalysisUnavailableReason);
            var location = status.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.True(location.X >= 0 && location.X + status.Bounds.Width <= window.ClientSize.Width);
            Assert.True(location.Y >= 0 && location.Y + status.Bounds.Height <= window.ClientSize.Height);
            var output = Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var bitmap = new RenderTargetBitmap(new PixelSize(320, 500));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, "available-analyses-avalonia.png"), PngBitmapEncoderOptions.Default);
            }
            var tooltipTargets = new[] { (Control)status, heading, (Control)row.Parent! };
            foreach (var target in tooltipTargets) ToolTip.SetShowDelay(target, 0);
            window.MouseMove(status.TranslatePoint(new Point(status.Bounds.Width - 5, status.Bounds.Height / 2), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Assert.True(tooltipTargets.Any(ToolTip.GetIsOpen), "Hovering the unavailable status must show its explanation.");
        }
        finally
        {
            window.Close();
            Application.Current.Styles.Remove(theme);
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        }
    }

    [AvaloniaFact]
    public void KnownThermochemistryEnablesProtonationViewWithoutAnUnavailableReason()
    {
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        try
        {
            var workspace = new AnalysisResultWorkspaceControl { Result = CreateResult(Buffer.MES, Buffer.Hepes) };
            Assert.Contains(ResultAnalysisViewMode.Protonation, workspace.AvailableViewModes);
            var section = (Control)typeof(AnalysisResultWorkspaceControl)
                .GetMethod("BuildAvailabilitySection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, null)!;
            Assert.DoesNotContain(section.GetLogicalDescendants().OfType<TextBlock>(),
                label => label.Text?.Contains("Protonation analysis requires") == true);
            var heading = section.GetLogicalDescendants().OfType<TextBlock>().Single(label => label.Text == "Protonation");
            var status = ((Grid)heading.Parent!).Children.OfType<TextBlock>().Last();
            Assert.Equal("Available", status.Text);
            Assert.Null(ToolTip.GetTip(heading));
            Assert.Null(ToolTip.GetTip(status));
        }
        finally { AnalysisResultWorkspaceControl.ResetSessionViewForTesting(); }
    }

    [AvaloniaTheory]
    [InlineData(0.5, 0.2, 0.9)]
    [InlineData(-0.5, -0.9, -0.2)]
    [InlineData(0.0, -0.2, 0.4)]
    public void OutputUsesSavedSignedUptakeAndCompleteInterval(double center, double lower, double upper)
    {
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        var result = CreateResult(Buffer.MES, Buffer.Hepes);
        var uptake = new FloatWithError(center, 0.1, lower, upper);
        var enthalpy = new FloatWithError(-40000, 500, -41200, -39400);
        result.ProtonationAnalysis.ProtonationChange = uptake;
        result.ProtonationAnalysis.BindingEnthalpy = enthalpy.Energy;
        result.ProtonationAnalysis.Fit = new LinearFitWithError(uptake, enthalpy, 0);
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
        var workspace = new AnalysisResultWorkspaceControl { Result = result };
        try
        {
            typeof(AnalysisResultWorkspaceControl).GetMethod("RefreshProtonationAnalysis",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, null);
            var labels = workspace.GetLogicalDescendants().OfType<TextBlock>().ToList();
            Assert.Contains(labels, label => label.Text == uptake.AsNumber());
            Assert.Contains(labels, label => label.Text == "Net proton uptake"
                && Equals(ToolTip.GetTip(label), "Positive: uptake; negative: release."));
            Assert.Contains(labels, label => label.Text == "Buffer-independent enthalpy");
        }
        finally
        {
            AppSettings.UncertaintyDisplayStyle = previousStyle;
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        }
    }

    static ExperimentData CreateData(Buffer? buffer, string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name, MeasuredTemperature = 25, TargetTemperature = 25,
            CellConcentration = new FloatWithError(30e-6), SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3,
        };
        if (buffer.HasValue)
        {
            var attribute = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            attribute.IntValue = (int)buffer.Value;
            attribute.DoubleValue = 7;
            attribute.ParameterValue = new FloatWithError(0.05);
            data.Attributes.Add(attribute);
        }
        return data;
    }

    static AnalysisResult CreateResult(params Buffer?[] buffers)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        var models = buffers.Select((buffer, index) =>
        {
            var model = new OneSetOfSites(CreateData(buffer, "experiment-" + (index + 1)));
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            return (Model)model;
        }).ToList();
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }
}
