using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AnalysisITC.Avalonia.ListItems;

public partial class DataListItemControl : UserControl
{
    public event EventHandler? MoreRequested;
    public event EventHandler? RemoveRequested;

    public Control MenuAnchor => MoreActionsButton;

    public DataListItemControl()
    {
        InitializeComponent();
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
