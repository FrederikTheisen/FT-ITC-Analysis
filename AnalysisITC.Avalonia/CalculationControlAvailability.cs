using System.Runtime.CompilerServices;
using Avalonia.Controls;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia;

internal static class CalculationControlAvailability
{
    sealed class SavedTooltip { public object? Value; public bool Eligible; public bool ShowOnDisabled; }
    static readonly ConditionalWeakTable<Control, SavedTooltip> tooltips = new();

    public static void Restore(Control control)
    {
        if (!tooltips.TryGetValue(control, out var original)) return;
        control.IsEnabled = original.Eligible;
        ToolTip.SetTip(control, original.Value);
        ToolTip.SetShowOnDisabled(control, original.ShowOnDisabled);
        tooltips.Remove(control);
    }

    public static void Update(Control control, bool eligible)
    {
        var busy = ProcessingActivity.IsBusy;
        control.IsEnabled = eligible && !busy;
        if (busy)
        {
            tooltips.GetValue(control, item => new SavedTooltip { Value = ToolTip.GetTip(item), Eligible = eligible, ShowOnDisabled = ToolTip.GetShowOnDisabled(item) });
            ToolTip.SetTip(control, FittingStatusPresentation.BusyTooltip);
            ToolTip.SetShowOnDisabled(control, true);
        }
        else if (tooltips.TryGetValue(control, out var original))
        {
            ToolTip.SetTip(control, original.Value);
            ToolTip.SetShowOnDisabled(control, original.ShowOnDisabled);
            tooltips.Remove(control);
        }
    }
}
