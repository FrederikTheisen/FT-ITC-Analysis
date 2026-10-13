using System;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class SupportingFigureSelectionStateTests
{
    [Fact]
    public void SettingsAreSnapshotsAndOnlyBelongToTheirDocument()
    {
        var state = new SupportingFigureSelectionState();
        var document = Guid.NewGuid();
        var options = new PublicationFigureCanvasOptions
        {
            PlotWidthCentimeters = 6, PlotHeightCentimeters = 8, Columns = 2, Rows = 4,
            FontSize = 9.5, SymbolSize = 5, StrokeWidth = 0.5,
            ShowPanelLetters = false, ShowPanelTitles = true, GroupResultFigures = false,
            ShowInformationBoxes = false, PanelLabelPrefix = "S"
        };
        state.Capture(document, Array.Empty<ITCDataContainer>(), options, 0.5);
        options.PlotWidthCentimeters = 10;

        var restored = state.RestoreOptions(document);
        Assert.Equal(6, restored.PlotWidthCentimeters);
        Assert.Equal(8, restored.PlotHeightCentimeters);
        Assert.Equal(2, restored.Columns);
        Assert.Equal(4, restored.Rows);
        Assert.Equal(9.5, restored.FontSize);
        Assert.Equal(5, restored.SymbolSize);
        Assert.Equal(0.5, restored.StrokeWidth);
        Assert.False(restored.ShowPanelLetters);
        Assert.True(restored.ShowPanelTitles);
        Assert.False(restored.GroupResultFigures);
        Assert.False(restored.ShowInformationBoxes);
        Assert.Equal("S", restored.PanelLabelPrefix);
        Assert.Equal(0.5, state.RestorePreviewZoom(document));
        restored.Columns = 6;
        Assert.Equal(2, state.RestoreOptions(document).Columns);

        var replacement = Guid.NewGuid();
        state.Restore(replacement, Array.Empty<ITCDataContainer>(), null);
        Assert.Equal(5, state.RestoreOptions(replacement).PlotWidthCentimeters);
        Assert.Equal(3, state.RestoreOptions(replacement).Columns);
        Assert.Equal(1, state.RestorePreviewZoom(replacement));
    }

    [Fact]
    public void IncompleteSettingsDoNotReplaceTheSavedValidSettings()
    {
        var state = new SupportingFigureSelectionState();
        var document = Guid.NewGuid();
        state.Capture(document, Array.Empty<ITCDataContainer>(), new PublicationFigureCanvasOptions { Columns = 2 });
        state.Capture(document, Array.Empty<ITCDataContainer>(), new PublicationFigureCanvasOptions { Columns = 0 }, null);

        Assert.Equal(2, state.RestoreOptions(document).Columns);
        Assert.Null(state.RestorePreviewZoom(document));
    }

    [Fact]
    public void ReopeningPreservesCompositionOrderAndIgnoresNewSidebarSelection()
    {
        var state = new SupportingFigureSelectionState();
        var document = Guid.NewGuid();
        var first = new ExperimentData("first.itc");
        var second = new ExperimentData("second.itc");
        var third = new ExperimentData("third.itc");
        var available = new[] { first, second, third };

        Assert.Equal(new[] { first }, state.Restore(document, available, first));
        state.Capture(document, new[] { second, first });

        Assert.Equal(new[] { second, first }, state.Restore(document, available, third));
    }

    [Fact]
    public void ReopeningAnIntentionallyEmptyCompositionKeepsItEmpty()
    {
        var state = new SupportingFigureSelectionState();
        var document = Guid.NewGuid();
        var first = new ExperimentData("first.itc");
        state.Capture(document, Array.Empty<ITCDataContainer>());

        Assert.Empty(state.Restore(document, new[] { first }, first));
    }

    [Fact]
    public void RemovedSourcesAreDiscardedWithoutChangingRemainingOrder()
    {
        var state = new SupportingFigureSelectionState();
        var document = Guid.NewGuid();
        var first = new ExperimentData("first.itc");
        var second = new ExperimentData("second.itc");
        var third = new ExperimentData("third.itc");
        state.Capture(document, new[] { third, second, first });

        Assert.Equal(new[] { third, first }, state.Restore(document, new[] { first, third }, first));
        Assert.Equal(new[] { third, first }, state.Restore(document, new[] { first, second, third }, first));
    }

    [Fact]
    public void ReplacementDocumentStartsFromItsCurrentSelectionEvenWithTheSameSources()
    {
        var state = new SupportingFigureSelectionState();
        var first = new ExperimentData("first.itc");
        var second = new ExperimentData("second.itc");
        state.Capture(Guid.NewGuid(), new[] { first });

        Assert.Equal(new[] { second }, state.Restore(Guid.NewGuid(), new[] { first, second }, second));
    }
}
