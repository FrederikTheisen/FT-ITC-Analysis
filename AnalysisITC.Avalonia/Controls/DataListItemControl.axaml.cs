using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AnalysisITC.Avalonia.ListItems;

public partial class DataListItemControl : UserControl
{
    public event EventHandler? MoreRequested;
    public event EventHandler? RemoveRequested;
    public event EventHandler<DataListDragRequestedEventArgs>? DragRequested;

    Point? dragStart;
    PointerPressedEventArgs? dragTrigger;

    public Control MenuAnchor => MoreActionsButton;

    public DataListItemControl()
    {
        InitializeComponent();
        PointerPressed += OnRowPointerPressed;
        PointerMoved += OnRowPointerMoved;
        PointerReleased += OnRowPointerReleased;
        PointerCaptureLost += OnRowPointerCaptureLost;
    }

    public void SetDropIndicator(bool before, bool after)
    {
        TopDropIndicator.IsVisible = before;
        BottomDropIndicator.IsVisible = after;
    }

    void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragStart = e.GetPosition(this);
        dragTrigger = e;
    }

    void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragStart == null || dragTrigger == null
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var current = e.GetPosition(this);
        var dx = current.X - dragStart.Value.X;
        var dy = current.Y - dragStart.Value.Y;
        if (Math.Sqrt(dx * dx + dy * dy) <= AvaloniaGraphSettings.ProcessingDragThreshold)
            return;

        var trigger = dragTrigger;
        ClearPendingDrag();
        DragRequested?.Invoke(this, new DataListDragRequestedEventArgs(trigger));
        e.Handled = true;
    }

    void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e) => ClearPendingDrag();

    void OnRowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearPendingDrag();

    void ClearPendingDrag()
    {
        dragStart = null;
        dragTrigger = null;
    }

    void OnInlineActionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    void OnMoreActionsClick(object? sender, RoutedEventArgs e)
    {
        MoreRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        RemoveRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

public sealed class DataListDragRequestedEventArgs : EventArgs
{
    public PointerPressedEventArgs TriggerEvent { get; }

    public DataListDragRequestedEventArgs(PointerPressedEventArgs triggerEvent)
    {
        TriggerEvent = triggerEvent;
    }
}
