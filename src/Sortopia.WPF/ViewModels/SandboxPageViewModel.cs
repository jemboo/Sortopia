using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class SandboxPageViewModel : ViewModelBase
{
    public SandboxPageViewModel(Action returnHome)
    {
        ReturnHomeCommand = new RelayCommand(returnHome);
    }

    public string Title => "Sandbox";

    public ICommand ReturnHomeCommand { get; }
}
