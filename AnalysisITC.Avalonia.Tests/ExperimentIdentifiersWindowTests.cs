using System.Collections;
using System.Linq;
using System.Reflection;

using Avalonia.Controls;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Core.Data;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ExperimentIdentifiersWindowTests
{
    [Fact]
    public void SharedInputsOnlyChangeNonblankFieldsAndRespectFillOrReplace()
    {
        Assert.Equal("Cell A", ExperimentIdentifiersWindow.ApplySharedValue("Cell A", "Cell B", replace: false));
        Assert.Equal("Cell B", ExperimentIdentifiersWindow.ApplySharedValue("", " Cell B ", replace: false));
        Assert.Equal("Cell B", ExperimentIdentifiersWindow.ApplySharedValue("Cell A", "Cell B", replace: true));
        Assert.Equal("Cell A", ExperimentIdentifiersWindow.ApplySharedValue("Cell A", " ", replace: true));
        Assert.Equal("", ExperimentIdentifiersWindow.ApplySharedValue("", " ", replace: true));
    }

    [Fact]
    public void ApplyCommitsStagedIdentifiersWhileSkipLeavesExperimentUnchanged()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
        var experiment = new ExperimentData("Identifiers-apply.itc")
        {
            ExternalExperimentId = "old experiment",
            CellSampleId = "old cell",
            SyringeSampleId = "old syringe"
        };
        var skipped = new ExperimentIdentifiersWindow(new[] { experiment });
        SetDraftValue(skipped, "ExperimentId", "pending experiment");
        skipped.Close();
        Assert.Equal("old experiment", experiment.ExternalExperimentId);

        var applied = new ExperimentIdentifiersWindow(new[] { experiment });
        SetDraftValue(applied, "ExperimentId", " new experiment ");
        SetDraftValue(applied, "CellId", "new cell");
        SetDraftValue(applied, "SyringeId", "new syringe");
        applied.Apply();

        Assert.Equal("new experiment", experiment.ExternalExperimentId);
        Assert.Equal("new cell", experiment.CellSampleId);
        Assert.Equal("new syringe", experiment.SyringeSampleId);
    }

    [Fact]
    public void SharedBulkActionsOnlyAffectSelectedRowsAndBlankInputsPreserveOtherIdentifiers()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
        var selected = new ExperimentData("Identifiers-selected.itc") { CellSampleId = "old cell", SyringeSampleId = "old syringe" };
        var unselected = new ExperimentData("Identifiers-unselected.itc") { CellSampleId = "other cell", SyringeSampleId = "other syringe" };
        var window = new ExperimentIdentifiersWindow(new[] { selected, unselected });
        SetDraftSelection(window, 1, false);
        window.ApplyShared("shared cell", "", replace: true);
        window.Apply();

        Assert.Equal("shared cell", selected.CellSampleId);
        Assert.Equal("old syringe", selected.SyringeSampleId);
        Assert.Equal("other cell", unselected.CellSampleId);
        Assert.Equal("other syringe", unselected.SyringeSampleId);

        var fillExperiment = new ExperimentData("Identifiers-fill.itc") { CellSampleId = "existing cell", SyringeSampleId = "" };
        var fillWindow = new ExperimentIdentifiersWindow(new[] { fillExperiment });
        fillWindow.ApplyShared("shared cell", "shared syringe", replace: false);
        fillWindow.Apply();
        Assert.Equal("existing cell", fillExperiment.CellSampleId);
        Assert.Equal("shared syringe", fillExperiment.SyringeSampleId);
    }

    [Fact]
    public void SelectAllReflectsRowSelectionAndSharedActionsNeedAValueAndACheckedRow()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
        var window = new ExperimentIdentifiersWindow(new[]
        {
            new ExperimentData("Identifiers-first.itc"),
            new ExperimentData("Identifiers-second.itc"),
        }, reviewsImport: true);

        Assert.True(window.SelectAllCheck.IsChecked);
        Assert.False(window.FillBlanksButton.IsEnabled);
        window.SharedCellIdBox.Text = "shared cell";
        Assert.True(window.FillBlanksButton.IsEnabled);
        Assert.True(window.ReplaceValuesButton.IsEnabled);

        SetDraftSelection(window, 1, false);
        Assert.Null(window.SelectAllCheck.IsChecked);
        SetDraftSelection(window, 0, false);
        Assert.False(window.SelectAllCheck.IsChecked);
        Assert.False(window.FillBlanksButton.IsEnabled);
    }

    static void SetDraftValue(ExperimentIdentifiersWindow window, string property, string value)
    {
        var draft = Drafts(window).Cast<object>().Single();
        var textBox = (TextBox)draft.GetType().GetProperty(property)!.GetValue(draft)!;
        textBox.Text = value;
    }

    static void SetDraftSelection(ExperimentIdentifiersWindow window, int index, bool selected)
    {
        var draft = Drafts(window).Cast<object>().ElementAt(index);
        ((CheckBox)draft.GetType().GetProperty("Selection")!.GetValue(draft)!).IsChecked = selected;
    }

    static IEnumerable Drafts(ExperimentIdentifiersWindow window) => (IEnumerable)typeof(ExperimentIdentifiersWindow)
        .GetField("drafts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
