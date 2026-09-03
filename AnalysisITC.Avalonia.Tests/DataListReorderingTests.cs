using Avalonia.Input;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class DataListReorderingTests
{
    [Fact]
    public void DragPayloadUsesPlatformSerializableApplicationFormat()
    {
        Assert.Equal(DataFormatKind.Application, MainWindow.DataListItemDragFormat.Kind);
    }

    [Theory]
    [InlineData(0, 0, 48, 4, 0)]
    [InlineData(0, 23, 48, 4, 0)]
    [InlineData(0, 24, 48, 4, 1)]
    [InlineData(2, 47, 48, 4, 3)]
    [InlineData(3, 48, 48, 4, 4)]
    public void CalculatesInsertionBoundaryFromRowMidpoint(
        int itemIndex,
        double pointerY,
        double itemHeight,
        int itemCount,
        int expected)
    {
        Assert.Equal(expected, MainWindow.CalculateDataListInsertionIndex(
            itemIndex, pointerY, itemHeight, itemCount));
    }

    [Theory]
    [InlineData(-1, 0, 48, 4)]
    [InlineData(4, 0, 48, 4)]
    [InlineData(0, 0, 0, 4)]
    public void InvalidRowsDoNotProduceInsertionBoundaries(
        int itemIndex,
        double pointerY,
        double itemHeight,
        int itemCount)
    {
        Assert.Equal(-1, MainWindow.CalculateDataListInsertionIndex(
            itemIndex, pointerY, itemHeight, itemCount));
    }
}
