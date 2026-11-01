using System.Collections.ObjectModel;
using Sortopia.Runs;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class OutputSlot(RunOutput output, int index) : ViewModelBase
{
    private bool included = true;
    private string slot = index.ToString();
    public RunOutput Output { get; } = output;
    public bool Included { get => included; set => SetProperty(ref included, value); }
    public string Slot { get => slot; set => SetProperty(ref slot, value); }
}

public sealed partial class SortableTestsPageViewModel : ViewModelBase
{
    private readonly RunLibrary library;
    private string generator = "Merge";
    private string channels = "8", ways = "2";
    private PrefixTests prefix;
    private string runName = "Sortable tests";
    private string manifestName = "";
    private string status = "";
    private bool publishAll = true;
    private SortableTestsRecipe? selectedRecipe;
    private CompletedRun? selectedRun;
    private OutputManifest? selectedManifest;
    private int tab;
    private CancellationTokenSource? running;

    public SortableTestsPageViewModel(Action back, RunLibrary library, Func<string?, string?>? pickFolder = null)
    {
        this.library = library;
        CsvInventory = new(pickFolder);
        Prefixes = PrefixTests.Available();
        prefix = Prefixes[0];
        Back = new(back);
        Add = new(AddRecipe, () => CanEdit);
        Remove = new(() => Plan.Remove(SelectedRecipe!), () => SelectedRecipe != null && CanEdit);
        Execute = new(async () => await ExecuteAsync(), () => CanEdit && Plan.Count > 0);
        SaveDefinition = new(() => SaveDraft(), () => CanEdit && Plan.Count > 0);
        GenerateParameters = new(async () => await GenerateParametersAsync(), () => CanEdit && SelectedDefinition != null);
        ExecuteParameters = new(async () => await ExecuteParametersAsync(), () => CanEdit && parametersReady);
        Cancel = new(() => { running?.Cancel(); Status = "Cancelling after the current generation step…"; }, () => !CanEdit);
        Publish = new(PublishSelection, () => SelectedRun != null);
        GenerateModels = new(async () => await GenerateModelsAsync(), () => CanEdit);
        EvaluateModels = new(async () => await EvaluateModelsAsync(), () => CanEdit && ModelsInput != null && TestsInput != null);
        Plan.CollectionChanged += (_, _) => { Execute.RaiseCanExecuteChanged(); SaveDefinition.RaiseCanExecuteChanged(); };
    }

    public RelayCommand Back { get; }
    public RelayCommand Add { get; }
    public RelayCommand Remove { get; }
    public RelayCommand Execute { get; }
    public RelayCommand SaveDefinition { get; }
    public RelayCommand GenerateParameters { get; }
    public RelayCommand ExecuteParameters { get; }
    public RelayCommand Cancel { get; }
    public RelayCommand Publish { get; }
    public IReadOnlyList<string> Generators { get; } = ["Merge", "Prefix"];
    public IReadOnlyList<PrefixTests> Prefixes { get; }
    public ObservableCollection<SortableTestsRecipe> Plan { get; } = [];
    public ObservableCollection<SortableTestsDirectRun> Definitions { get; } = [];
    public ObservableCollection<CompletedRun> Runs { get; } = [];
    public ObservableCollection<OutputManifest> Manifests { get; } = [];
    public ObservableCollection<OutputSlot> Slots { get; } = [];
    public string LibraryPath => library.DirectoryPath;
    public bool CanEdit => running == null;
    private SortableTestsDirectRun? selectedDefinition;
    private bool parametersReady;
    public SortableTestsDirectRun? SelectedDefinition
    {
        get => selectedDefinition;
        set
        {
            if (SetProperty(ref selectedDefinition, value)) RefreshParameterState();
        }
    }

    private void RefreshParameterState()
    {
        parametersReady = SelectedDefinition != null && library.HasSortableTestsParameters(SelectedDefinition);
        GenerateParameters.RaiseCanExecuteChanged();
        ExecuteParameters.RaiseCanExecuteChanged();
    }
    public int Tab { get => tab; set => SetProperty(ref tab, value); }
    public CsvInventoryViewModel CsvInventory { get; }
    private int designerTab;
    public int DesignerTab { get => designerTab; set => SetProperty(ref designerTab, value); }
    public string RunName { get => runName; set => SetProperty(ref runName, value); }
    public string ManifestName { get => manifestName; set => SetProperty(ref manifestName, value); }
    public bool PublishAll { get => publishAll; set => SetProperty(ref publishAll, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Generator
    {
        get => generator;
        set { if (SetProperty(ref generator, value)) { OnPropertyChanged(nameof(IsMerge)); OnPropertyChanged(nameof(IsPrefix)); } }
    }
    public bool IsMerge => Generator == "Merge";
    public bool IsPrefix => Generator == "Prefix";
    public string Channels { get => channels; set => SetProperty(ref channels, value); }
    public string Ways { get => ways; set => SetProperty(ref ways, value); }
    public PrefixTests Prefix { get => prefix; set => SetProperty(ref prefix, value); }

    private void AddRecipe()
    {
        try
        {
            if (IsPrefix) Plan.Add(Prefix);
            else
            {
                if (!int.TryParse(Channels, out var width) || !int.TryParse(Ways, out var parts))
                    throw new ArgumentException("Enter whole numbers for channels and merge ways.");
                var merge = new MergeTests(width, parts);
                merge.Validate();
                Plan.Add(merge);
            }
            Status = "";
        }
        catch (ArgumentException error) { Status = error.Message; }
    }

    public SortableTestsRecipe? SelectedRecipe
    {
        get => selectedRecipe;
        set { SetProperty(ref selectedRecipe, value); Remove.RaiseCanExecuteChanged(); }
    }
    public CompletedRun? SelectedRun
    {
        get => selectedRun;
        set
        {
            if (!SetProperty(ref selectedRun, value)) return;
            SelectedManifest = null;
            Manifests.Clear();
            ResetSlots();
            if (value != null)
                try { foreach (var m in library.Manifests(value.Id)) Manifests.Add(m); }
                catch (Exception e) { Status = e.Message; }
            Publish.RaiseCanExecuteChanged();
        }
    }
    public OutputManifest? SelectedManifest
    {
        get => selectedManifest;
        set
        {
            if (!SetProperty(ref selectedManifest, value)) return;
            ResetSlots();
            if (value == null) return;
            ManifestName = value.Name;
            foreach (var slot in Slots)
            {
                var entry = value.Entries.FirstOrDefault(e => e.ArtifactId == slot.Output.ArtifactId);
                slot.Included = entry != null;
                if (entry != null) slot.Slot = entry.Index.ToString();
            }
        }
    }

    private void ResetSlots()
    {
        Slots.Clear();
        ManifestName = SelectedRun?.Name ?? "";
        if (SelectedRun != null)
            foreach (var output in SelectedRun.Outputs) Slots.Add(new(output, Slots.Count));
    }

    public void Open()
    {
        try
        {
            var previousDefinition = SelectedDefinition?.Id;
            var definitions = library.SortableTestsDefinitions();
            Definitions.Clear();
            foreach (var definition in definitions) Definitions.Add(definition);
            SelectedDefinition = Definitions.FirstOrDefault(d => d.Id == previousDefinition) ?? Definitions.FirstOrDefault();
            RefreshParameterState();
            var saved = library.CompletedRuns();
            var previous = SelectedRun?.Id;
            Runs.Clear();
            foreach (var run in saved) Runs.Add(run);
            SelectedRun = Runs.FirstOrDefault(r => r.Id == previous) ?? Runs.FirstOrDefault();
            RefreshInputs();
        }
        catch (Exception error) { Status = error.Message; }
    }

    private SortableTestsDirectRun? SaveDraft()
    {
        try
        {
            var definition = library.SaveSortableTestsDefinition(RunName, Plan);
            Definitions.Insert(0, definition);
            SelectedDefinition = definition;
            Status = $"Saved {definition.Name}. Generate its runParameter files to prepare it for execution.";
            return definition;
        }
        catch (Exception error) { Status = error.Message; return null; }
    }

    internal Task GenerateParametersAsync()
    {
        if (SelectedDefinition is not { } run) return Task.CompletedTask;
        return RunWorkAsync((token, _) =>
        {
            library.GenerateSortableTestsParameters(run.Id, token);
            return null;
        }, $"Prepared {run.Datasets.Length} runParameter files for {run.Name}.");
    }

    internal Task ExecuteParametersAsync()
    {
        if (SelectedDefinition is not { } run) return Task.CompletedTask;
        var publish = PublishAll;
        return RunWorkAsync((token, progress) => library.ExecuteSortableTests(run.Id, publish, token, progress));
    }

    internal Task ExecuteAsync()
    {
        if (!CanEdit || SaveDraft() is not { } run) return Task.CompletedTask;
        var publish = PublishAll;
        return RunWorkAsync((token, progress) =>
        {
            library.GenerateSortableTestsParameters(run.Id, token);
            return library.ExecuteSortableTests(run.Id, publish, token, progress);
        });
    }

    private Task RunAsync(string name, RunRecipe[] recipes, bool all) =>
        RunWorkAsync((token, progress) => library.Execute(name, recipes, all, token, progress));

    private async Task RunWorkAsync(Func<CancellationToken, IProgress<string>, CompletedRun?> work, string message = "")
    {
        if (!CanEdit) return;
        using var cancellation = new CancellationTokenSource();
        running = cancellation;
        NotifyRunning();
        var progress = new Progress<string>(message => { if (running == cancellation) Status = message; });
        try
        {
            var run = await Task.Run(() => work(cancellation.Token, progress));
            if (run == null) { Status = message; return; }
            var previous = Runs.FirstOrDefault(r => r.Id == run.Id);
            if (previous != null) Runs.Remove(previous);
            Runs.Insert(0, run);
            SelectedRun = run;
            RefreshInputs();
            if (run.Outputs[0].DataType == OutputDataType.SorterModelSet)
                ModelsInput = ModelChoices.FirstOrDefault(choice => choice.Slot.RunId == run.Id);
            DesignerTab = 0;
            Tab = 1;
            Status = run.Outputs[0].Evaluation is { } result ? $"Completed {run.Name} · {result}"
                : $"Completed {run.Name} · {run.Outputs.Length} outputs saved.";
        }
        catch (OperationCanceledException) { Status = "Run cancelled."; }
        catch (Exception error) { Status = error.Message; }
        finally { running = null; RefreshParameterState(); NotifyRunning(); }
    }

    private void NotifyRunning()
    {
        OnPropertyChanged(nameof(CanEdit));
        foreach (var command in new[] { Add, Remove, Execute, SaveDefinition, GenerateParameters, ExecuteParameters,
            Cancel, GenerateModels, EvaluateModels }) command.RaiseCanExecuteChanged();
    }

    private void PublishSelection()
    {
        try
        {
            var entries = Slots.Where(s => s.Included).Select(s =>
            {
                if (!int.TryParse(s.Slot, out var index) || index < 0)
                    throw new ArgumentException("Each selected output needs a nonnegative integer index.");
                return new ManifestEntry(s.Output.DataType, index, s.Output.ArtifactId);
            });
            var manifest = library.Publish(SelectedRun!.Id, ManifestName, entries);
            Manifests.Add(manifest);
            SelectedManifest = manifest;
            RefreshInputs();
            Status = $"Published {manifest.Name} · {manifest.Entries.Length} slots.";
        }
        catch (Exception error) { Status = error.Message; }
    }

    public void Stop() => running?.Cancel();
}
