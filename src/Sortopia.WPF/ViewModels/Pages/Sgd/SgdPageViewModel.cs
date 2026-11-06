using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Pages.Sgd.SubPages;

namespace Sortopia.WPF.ViewModels.Pages.Sgd;

public enum SgdStep { Setup = 1, Sampling = 2, Evolution = 3 }

/// <summary>Owns the three persistent SGD subpages and coordinates navigation and sorter handoff.</summary>
public sealed class SgdPageViewModel : ViewModelBase
{
    private readonly SgdOperation operation = new();
    private SgdStep currentStep = SgdStep.Setup;

    public SgdPageViewModel(Action returnHome)
    {
        Setup = new(operation, () => SelectStep(SgdStep.Sampling));
        Sampling = new(Setup, operation, EvolveSelection);
        Evolution = new(Setup, operation);
        SetupStepCommand = new RelayCommand(() => SelectStep(SgdStep.Setup), () => operation.IsIdle);
        SamplingStepCommand = new RelayCommand(() => SelectStep(SgdStep.Sampling), () => operation.IsIdle && Setup.IsValid);
        EvolutionStepCommand = new RelayCommand(OpenEvolution, () => CanOpenEvolution);
        ReturnHomeCommand = new RelayCommand(() => { Stop(); returnHome(); });
        operation.Changed += NotifyNavigation;
        Setup.PropertyChanged += (_, _) => NotifyNavigation();
        Sampling.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(Sampling.LatestCandidate) or nameof(Sampling.GeneratedCount)) NotifyNavigation();
        };
        Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Evolution.HasParent)) NotifyNavigation();
        };
    }

    public SgdSetupPageViewModel Setup { get; }
    public SgdSamplingPageViewModel Sampling { get; }
    public SgdEvolutionPageViewModel Evolution { get; }
    public SgdStep CurrentStep => currentStep;
    public ViewModelBase CurrentPage => CurrentStep switch
    {
        SgdStep.Setup => Setup,
        SgdStep.Sampling => Sampling,
        _ => Evolution
    };
    public RelayCommand SetupStepCommand { get; }
    public RelayCommand SamplingStepCommand { get; }
    public RelayCommand EvolutionStepCommand { get; }
    public RelayCommand ReturnHomeCommand { get; }

    private bool CanOpenEvolution => Setup.IsValid && (Evolution.HasParent || Sampling.LatestMatchesSetup)
        && (operation.IsIdle || operation.Activity == SgdActivity.Sample && Sampling.GeneratedCount > 0);

    private void OpenEvolution()
    {
        Stop();
        if (!Evolution.HasParent) Evolution.SetParent(Sampling.LatestCandidate!);
        SelectStep(SgdStep.Evolution);
    }

    private void EvolveSelection(SearchCandidate parent)
    {
        // Capture the inspected sample before cancellation; later sampling results cannot replace it.
        Stop();
        Evolution.SetParent(parent);
        SelectStep(SgdStep.Evolution);
    }

    private void SelectStep(SgdStep step)
    {
        if (SetProperty(ref currentStep, step, nameof(CurrentStep))) OnPropertyChanged(nameof(CurrentPage));
        NotifyNavigation();
    }

    public void Stop() => operation.Stop();

    private void NotifyNavigation()
    {
        SetupStepCommand.RaiseCanExecuteChanged();
        SamplingStepCommand.RaiseCanExecuteChanged();
        EvolutionStepCommand.RaiseCanExecuteChanged();
    }
}
