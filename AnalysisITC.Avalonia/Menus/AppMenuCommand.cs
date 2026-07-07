using System;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Input;

namespace AnalysisITC.Avalonia.Menus;

internal sealed class AppMenuCommand : ICommand
{
    readonly Func<Task> execute;
    readonly Func<bool> canExecute;
    readonly Func<bool>? isChecked;

    public event EventHandler? CanExecuteChanged;

    public AppMenuCommand(
        string id,
        string title,
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Func<bool>? isChecked = null,
        KeyGesture? gesture = null)
    {
        Id = id;
        Title = title;
        this.execute = execute;
        this.canExecute = canExecute ?? (() => true);
        this.isChecked = isChecked;
        Gesture = gesture;
    }

    public string Id { get; }
    public string Title { get; }
    public KeyGesture? Gesture { get; }
    public bool HasCheckState => isChecked != null;
    public bool IsChecked => isChecked?.Invoke() == true;

    public bool CanExecute(object? parameter) => canExecute();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        await execute();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
