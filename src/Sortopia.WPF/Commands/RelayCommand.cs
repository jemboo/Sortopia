using System.Windows.Input;

namespace Sortopia.WPF.Commands;

public sealed class RelayCommand(Action<object?> execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action<object?> action = execute ?? throw new ArgumentNullException(nameof(execute));
    public RelayCommand(Action execute, Func<bool>? canExecute = null) : this(_ => execute(), canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) action(parameter);
    }
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
