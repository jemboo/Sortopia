using System.Data;
using System.IO;
using Sortopia.Runs;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class CsvInventoryViewModel : ViewModelBase
{
    private readonly Func<string?, string?>? pickFolder;
    private ProjectInventory? project;
    private DataView? rows;
    private string manifestName = "", status = "";
    private bool busy;

    public CsvInventoryViewModel(Func<string?, string?>? pickFolder)
    {
        this.pickFolder = pickFolder;
        Choose = new(async () => {
            var selected = this.pickFolder?.Invoke(project?.ProjectDirectory);
            if (selected != null) await OpenAsync(selected);
        }, () => !busy && this.pickFolder != null);
        Refresh = new(async () => await LoadAsync(project!, true), () => !busy && project != null);
        Publish = new(Save, () => !busy && rows?.Count > 0);
    }

    public RelayCommand Choose { get; }
    public RelayCommand Refresh { get; }
    public RelayCommand Publish { get; }
    public string? InventoryPath => project?.InventoryPath;
    public DataView? Rows { get => rows; private set => SetProperty(ref rows, value); }
    public string ManifestName { get => manifestName; set => SetProperty(ref manifestName, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }

    public async Task OpenAsync(string folder)
    {
        if (busy) return;
        try { await LoadAsync(new ProjectInventory(folder), false); }
        catch (Exception e) { Status = e.Message; }
    }

    private async Task LoadAsync(ProjectInventory inventory, bool refresh)
    {
        busy = true;
        CommandsChanged();
        Status = refresh ? "Reading GeneSort Results…" : "Opening project inventory…";
        try
        {
            var table = await Task.Run(() => refresh ? inventory.Refresh() : inventory.Open());
            foreach (DataColumn column in table.Columns) column.ReadOnly = true;
            table.Columns.Add("Include", typeof(bool)).SetOrdinal(0);
            table.Columns.Add("Order", typeof(string)).SetOrdinal(1);
            foreach (var group in table.Rows.Cast<DataRow>().GroupBy(row => (string)row["OutputDataType"]))
            {
                int index = 0;
                foreach (var row in group) { row["Include"] = true; row["Order"] = (index++).ToString(); }
            }
            project = inventory;
            OnPropertyChanged(nameof(InventoryPath));
            Rows = table.DefaultView;
            ManifestName = "";
            Status = $"{table.Rows.Count} Results in the inventory.";
        }
        catch (Exception e) { Status = e.Message; }
        finally { busy = false; CommandsChanged(); }
    }

    private void Save()
    {
        try
        {
            var selection = Rows!.Cast<DataRowView>().Where(row => (bool)row["Include"]).Select(row =>
            {
                if (!int.TryParse(row["Order"].ToString(), out var order) || order < 0)
                    throw new ArgumentException("Enter a nonnegative order for each included Result.");
                return new InventorySelection((string)row["OutputDataType"], Guid.Parse((string)row["DataId"]), order);
            });
            var path = project!.SaveManifest(ManifestName, selection);
            Status = $"Saved {path}";
        }
        catch (Exception e) { Status = e.Message; }
    }

    private void CommandsChanged()
    {
        Choose.RaiseCanExecuteChanged();
        Refresh.RaiseCanExecuteChanged();
        Publish.RaiseCanExecuteChanged();
    }
}
