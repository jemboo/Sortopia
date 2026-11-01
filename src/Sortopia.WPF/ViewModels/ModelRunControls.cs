using Sortopia.Runs;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed record ManifestChoice(ManifestSlot Slot, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class SortableTestsPageViewModel
{
    public RelayCommand GenerateModels { get; }
    public RelayCommand EvaluateModels { get; }
    public string ModelRunName { get; set; } = "Sorter model set";
    public string ModelChannels { get; set; } = "24";
    public string ModelStages { get; set; } = "32";
    public string ModelCount { get; set; } = "16";
    public string ModelFirstIndex { get; set; } = "0";
    public string EvalRunName { get; set; } = "Sorter evaluation";
    public ManifestChoice[] ModelChoices { get; private set; } = [];
    public ManifestChoice[] TestChoices { get; private set; } = [];
    private ManifestChoice? modelsInput, testsInput;
    public ManifestChoice? ModelsInput
    {
        get => modelsInput;
        set { SetProperty(ref modelsInput, value); EvaluateModels.RaiseCanExecuteChanged(); }
    }
    public ManifestChoice? TestsInput
    {
        get => testsInput;
        set { SetProperty(ref testsInput, value); EvaluateModels.RaiseCanExecuteChanged(); }
    }

    private void RefreshInputs()
    {
        var previousModels = ModelsInput?.Slot;
        var previousTests = TestsInput?.Slot;
        var models = new List<ManifestChoice>();
        var tests = new List<ManifestChoice>();
        foreach (var run in Runs)
        {
            var outputs = run.Outputs.ToDictionary(o => o.ArtifactId);
            OutputManifest[] manifests;
            try { manifests = library.Manifests(run.Id); }
            catch (System.Text.Json.JsonException)
            {
                Status = $"Could not read the manifests for {run.Name} ({run.Id:N}).";
                continue;
            }
            foreach (var manifest in manifests)
            foreach (var entry in manifest.Entries)
            {
                if (entry.DataType == OutputDataType.SorterSetEval) continue;
                var label = $"{run.Name} / {manifest.Name} ({manifest.Id.ToString("N")[..8]}) · slot {entry.Index} · {outputs[entry.ArtifactId]}";
                var choice = new ManifestChoice(new(run.Id, manifest.Id, entry.Index), label);
                if (entry.DataType == OutputDataType.SorterModelSet) models.Add(choice);
                else if (entry.DataType == OutputDataType.SortableTestsWithPrefix) tests.Add(choice);
            }
        }
        ModelChoices = models.ToArray();
        TestChoices = tests.ToArray();
        OnPropertyChanged(nameof(ModelChoices));
        OnPropertyChanged(nameof(TestChoices));
        ModelsInput = ModelChoices.FirstOrDefault(c => c.Slot == previousModels) ?? ModelChoices.FirstOrDefault();
        TestsInput = TestChoices.FirstOrDefault(c => c.Slot == previousTests) ?? TestChoices.FirstOrDefault();
    }

    internal Task GenerateModelsAsync()
    {
        if (!int.TryParse(ModelChannels, out var channels) || !int.TryParse(ModelStages, out var stages) ||
            !int.TryParse(ModelCount, out var count) || !int.TryParse(ModelFirstIndex, out var first))
        {
            Status = "Enter whole numbers for channels, stages, model count and first index.";
            return Task.CompletedTask;
        }
        return RunAsync(ModelRunName, [new ModelSetRecipe(channels, stages, count, first)], true);
    }

    internal Task EvaluateModelsAsync() => ModelsInput == null || TestsInput == null
        ? Task.CompletedTask
        : RunAsync(EvalRunName, [new EvalRecipe(ModelsInput.Slot, TestsInput.Slot)], true);
}
