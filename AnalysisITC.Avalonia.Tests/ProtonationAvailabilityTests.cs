using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProtonationAvailabilityTests
{
    [AvaloniaFact]
    public void MissingThermochemistryShowsSharedReasonAndOmitsProtonationView()
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
        var window = new Window { Width = 320, Height = 500, Content = section };
        window.Show();
        try
        {
            window.UpdateLayout();
            var labels = section.GetLogicalDescendants().OfType<TextBlock>().ToList();
            var reason = Assert.Single(labels, label => label.Text == result.ProtonationAnalysisUnavailableReason);
            Assert.Contains(longName, reason.Text);
            Assert.Equal(TextWrapping.Wrap, reason.TextWrapping);
            Assert.True(reason.Bounds.Height > reason.FontSize * 3, "Long reason must occupy multiple lines.");
            var location = reason.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.True(location.X >= 0 && location.X + reason.Bounds.Width <= window.ClientSize.Width);
            Assert.True(location.Y >= 0 && location.Y + reason.Bounds.Height <= window.ClientSize.Height);
            var output = Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var bitmap = new RenderTargetBitmap(new PixelSize(320, 500));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, "cr021-avalonia-reason.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); AnalysisResultWorkspaceControl.ResetSessionViewForTesting(); }
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
        }
        finally { AnalysisResultWorkspaceControl.ResetSessionViewForTesting(); }
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
