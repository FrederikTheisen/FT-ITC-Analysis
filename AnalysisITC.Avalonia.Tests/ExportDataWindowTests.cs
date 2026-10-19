using System.Linq;
using System.Reflection;

using Avalonia.Controls;
using Avalonia.Layout;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Core.Export;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ExportDataWindowTests
{
    readonly ITestOutputHelper output;

    public ExportDataWindowTests(ITestOutputHelper output) => this.output = output;

    [AvaloniaFact]
    public void CombinedDataShowsSharedDescriptionAndEnergyUnitWithoutCorrectionOptions()
    {
        var dialog = CreateDialog(ExportType.InterchangeCsv);
        var description = GetField<TextBlock>(dialog, "descriptionText");
        var options = GetField<StackPanel>(dialog, "optionsPanel");
        var energyUnitRow = GetField<Control>(dialog, "energyUnitRow");
        var combinedDescription = typeof(ExportType)
            .GetField(nameof(ExportType.InterchangeCsv))!
            .GetCustomAttribute<ExportTypeAttribute>()!
            .Description;

        Assert.Equal(combinedDescription, description.Text);
        Assert.Contains("without fitted-offset correction", description.Text);
        Assert.Collection(options.Children, child => Assert.Same(energyUnitRow, child));
    }

    [AvaloniaFact]
    public void CombinedDataDescriptionAndOptionsFitDialogHeight()
    {
        var dialog = CreateDialog(ExportType.InterchangeCsv);
        try
        {
            dialog.Show();
            dialog.UpdateLayout();

            var description = GetField<TextBlock>(dialog, "descriptionText");
            var options = GetField<StackPanel>(dialog, "optionsPanel");
            var root = Assert.IsType<DockPanel>(dialog.Content);
            var footer = Assert.Single(root.Children.OfType<StackPanel>(), panel =>
                panel.Orientation == Orientation.Horizontal);

            Assert.True(description.Bounds.Height >= description.DesiredSize.Height,
                $"Description needs {description.DesiredSize.Height:0.##} px, but received {description.Bounds.Height:0.##} px.");
            Assert.True(options.Bounds.Height >= options.DesiredSize.Height,
                $"Options need {options.DesiredSize.Height:0.##} px, but received {options.Bounds.Height:0.##} px.");
            Assert.True(footer.Bounds.Bottom <= dialog.ClientSize.Height,
                $"Footer ends at {footer.Bounds.Bottom:0.##} px, beyond the {dialog.ClientSize.Height:0.##} px dialog height.");

            output.WriteLine($"Dialog client height: {dialog.ClientSize.Height:0.##} px; footer bottom: {footer.Bounds.Bottom:0.##} px.");
            output.WriteLine($"Combined description: {description.DesiredSize.Width:0.##} × {description.DesiredSize.Height:0.##} px; allocated {description.Bounds.Width:0.##} × {description.Bounds.Height:0.##} px.");
            output.WriteLine($"Options: {options.DesiredSize.Width:0.##} × {options.DesiredSize.Height:0.##} px; allocated {options.Bounds.Width:0.##} × {options.Bounds.Height:0.##} px.");
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(ExportType.Data, false, true)]
    [InlineData(ExportType.Peaks, true, true)]
    [InlineData(ExportType.InterchangeCsv, true, false)]
    [InlineData(ExportType.ITCsim, true, true)]
    public void EnergyAndCorrectionOptionsMatchSupportedFormats(
        ExportType format,
        bool hasEnergyUnit,
        bool hasCorrectionOption)
    {
        var dialog = CreateDialog(format);
        var options = GetField<StackPanel>(dialog, "optionsPanel");
        var energyUnitRow = GetField<Control>(dialog, "energyUnitRow");

        Assert.Equal(hasEnergyUnit, options.Children.Contains(energyUnitRow));
        Assert.Equal(hasCorrectionOption, options.Children.OfType<CheckBox>().Any());
    }

    static ExportDataWindow CreateDialog(ExportType format)
    {
        var constructor = typeof(ExportDataWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(ExportAccessoryViewSettings) },
            modifiers: null)!;
        return (ExportDataWindow)constructor.Invoke(new object[]
        {
            ExportAccessoryViewSettings.CreateDefault(format)
        });
    }

    static T GetField<T>(object target, string name) where T : class =>
        (T)typeof(ExportDataWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
