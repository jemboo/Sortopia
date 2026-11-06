using System.Runtime.Intrinsics.X86;
using Sortopia.WPF.Commands;
using Sortopia.WPF.ViewModels.Shared.Controls;

namespace Sortopia.WPF.ViewModels.Pages.Sgd.SubPages;

public sealed class SgdSetupPageViewModel : ViewModelBase
{
    private readonly SgdOperation operation;
    private bool useAvx512 = Avx512F.IsSupported;

    internal SgdSetupPageViewModel(SgdOperation operation, Action continueToSampling)
    {
        this.operation = operation;
        ContinueCommand = new RelayCommand(continueToSampling, () => CanEdit && IsValid);
        operation.Changed += NotifyState;
        Generator.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Generator.ChannelCount))
            {
                operation.Stop();
                Generator.SetTestWidth(Generator.ChannelCount);
            }
            if (change.PropertyName is nameof(Generator.SetupError) or nameof(Generator.TestInputError))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(PlannedInputs));
                OnPropertyChanged(nameof(PlannedCoverage));
                ContinueCommand.RaiseCanExecuteChanged();
            }
        };
    }

    public NetworkGeneratorSettings Generator { get; } = new(configureSimpleGenerator: true);
    public RelayCommand ContinueCommand { get; }
    public bool CanEdit => operation.IsIdle;
    public bool IsValid => Generator.SetupError.Length == 0 && Generator.TestInputError.Length == 0;
    public int? PlannedInputs => Generator.TestInputError.Length != 0 ? null
        : Generator.SamplesFor(Generator.ChannelCount) ?? (1 << Generator.ChannelCount);
    public string PlannedCoverage => Generator.TestInputError.Length != 0 ? "—" : Generator.AllSamples ? "Exhaustive" : "Sampled";
    public bool CanChooseAvx512 => Avx512F.IsSupported && CanEdit;
    public string Avx512Hint => Avx512F.IsSupported
        ? "Use Sortopia.Evaluation to test 512 binary inputs per vector. Choose before starting a run."
        : "AVX-512 is unavailable on this machine.";
    public bool UseAvx512
    {
        get => useAvx512;
        set { if (CanChooseAvx512) SetProperty(ref useAvx512, value); }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanChooseAvx512));
        ContinueCommand.RaiseCanExecuteChanged();
    }
}
