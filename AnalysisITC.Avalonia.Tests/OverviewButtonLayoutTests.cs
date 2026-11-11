using System;
using System.Linq;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class OverviewButtonLayoutTests
{
    [AvaloniaTheory]
    [InlineData("WelcomeOpenButton", false)]
    [InlineData("WelcomeReloadButton", false)]
    [InlineData("OverviewRawButton", true)]
    [InlineData("OverviewInjectionsButton", true)]
    public void OverviewButtonLabelsFitAndAreCentered(string buttonName, bool loadExperiment)
    {
        var previousPath = FTITCFormat.CurrentAccessedAppDocumentPath;
        DataManager.Clear(DataClearMode.ResetSession);
        var application = Application.Current!;
        var theme = new FluentTheme();
        application.Styles.Add(theme);
        var window = new MainWindow(false);
        try
        {
            window.SetStartupReady();
            window.Show();
            if (loadExperiment)
            {
                var experiment = new ExperimentData("overview.itc");
                experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6));
                DataManager.AddData(experiment);
                Dispatcher.UIThread.RunJobs();
                window.FindControl<ListBox>("ItemsList")!.SelectedIndex = 0;
            }
            window.UpdateLayout();

            var button = window.FindControl<Button>(buttonName)!;
            Assert.True(button.IsEffectivelyVisible);
            Assert.Contains(window, button.GetVisualAncestors());
            var presenter = Assert.Single(button.GetVisualDescendants().OfType<ContentPresenter>());
            var label = Assert.IsAssignableFrom<TextBlock>(presenter.Child);
            var labelCenter = new Point(label.Bounds.Width / 2, label.Bounds.Height / 2);
            var center = label.TranslatePoint(labelCenter, button)!.Value;
            Assert.InRange(Math.Abs(center.X - button.Bounds.Width / 2), 0, 1);
            Assert.InRange(Math.Abs(center.Y - button.Bounds.Height / 2), 0, 1);
            Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1);
            Assert.Equal(WorkspaceControlBuilder.ButtonPadding, presenter.Padding);
            Assert.Equal(VerticalAlignment.Center, presenter.VerticalContentAlignment);
        }
        finally
        {
            DocumentDirtyTracker.MarkClean();
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            FTITCFormat.CurrentAccessedAppDocumentPath = previousPath;
            Dispatcher.UIThread.RunJobs();
            application.Styles.Remove(theme);
        }
    }
}
