using System.Linq;

using AnalysisITC.Avalonia.ListItems;
using AnalysisITC.Core.Data;

using Avalonia.Controls;
using Avalonia.VisualTree;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DataListTitleLayoutTests
{
    [AvaloniaFact]
    public void WrappedTitleFitsDescendersOnItsSecondLine()
    {
        var experiment = new ExperimentData("gypq gypq gypq gypq gypq gypq gypq gypq gypq.itc");
        var entry = MainWindow.DataListEntry.From(experiment);
        var row = new DataListItemControl { DataContext = entry };
        var window = new Window { Width = 300, Height = 200, Content = row };
        try
        {
            window.Show();
            window.UpdateLayout();

            var title = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == entry.Title);
            var lines = title.TextLayout.TextLines;
            Assert.Equal(2, lines.Count);
            var inkBottom = title.TextLayout.Height + lines[1].OverhangAfter;
            Assert.True(inkBottom <= title.Bounds.Height, $"Title ink ends at {inkBottom}, beyond its {title.Bounds.Height}-pixel bounds.");
        }
        finally
        {
            window.Close();
        }
    }
}
