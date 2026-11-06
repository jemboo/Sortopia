using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

public enum SgdStep { Setup = 1, Sampling = 2, Evolution = 3 }

/// <summary>Navigation and sorter handoff for the three persistent SGD pages.</summary>
public sealed class SgdPageViewModel : ViewModelBase
{
    private readonly SgdOperation operation = new();
    private SgdStep currentStep = SgdStep.Setup;
    private string projectStatus = "Open a project folder to work with runs.";

    public SgdPageViewModel(Action returnHome, Action? openSortableTests = null, Action? openProjectFolder = null)
    {
        Setup = new(operation, () => SelectStep(SgdStep.Sampling));
        Sampling = new(Setup, operation, EvolveSelection);
        Evolution = new(Setup, operation);
        SetupStepCommand = new RelayCommand(() => SelectStep(SgdStep.Setup), () => operation.IsIdle);
        SamplingStepCommand = new RelayCommand(() => SelectStep(SgdStep.Sampling), () => operation.IsIdle && Setup.IsValid);
        EvolutionStepCommand = new RelayCommand(OpenEvolution, () => CanOpenEvolution);
        ReturnHomeCommand = new RelayCommand(() => { Stop(); returnHome(); });
        OpenSortableTestsCommand = new RelayCommand(() => { Stop(); openSortableTests?.Invoke(); }, () => openSortableTests is not null);
        OpenProjectFolderCommand = new RelayCommand(() => { Stop(); openProjectFolder?.Invoke(); }, () => openProjectFolder is not null);
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
    public RelayCommand OpenSortableTestsCommand { get; }
    public RelayCommand OpenProjectFolderCommand { get; }
    public string ProjectStatus { get => projectStatus; internal set => SetProperty(ref projectStatus, value); }

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
