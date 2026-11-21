using System.Globalization;
using Sortopia.Core;
using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Simple interval controls with the full IntSampleMethod syntax available for advanced schedules.</summary>
public sealed class WorkspaceReportingSchedule : ViewModelBase
{
    private readonly Action changed;
    private readonly Func<bool> canEdit;
    private string mode = "Every N generations", interval, growth = "0.1", custom = "Constant(1)";
    public WorkspaceReportingSchedule(string title, string interval, Func<bool> canEdit, Action changed)
    { Title = title; this.interval = interval; this.canEdit = canEdit; this.changed = changed; }
    public string Title { get; }
    public string[] Modes { get; } = ["Every N generations", "Increasing intervals", "Custom schedule"];
    public string Mode { get => mode; set { if (canEdit() && SetProperty(ref mode, value)) Notify(); } }
    public string Interval { get => interval; set { if (canEdit() && SetProperty(ref interval, value)) Notify(); } }
    public string Growth { get => growth; set { if (canEdit() && SetProperty(ref growth, value)) Notify(); } }
    public string Custom { get => custom; set { if (canEdit() && SetProperty(ref custom, value)) Notify(); } }
    public bool IsCustom => Mode == Modes[2];
    public bool HasInterval => !IsCustom;
    public bool IsIncreasing => Mode == Modes[1];
    public string IntervalLabel => IsIncreasing ? "First interval" : "Every";
    public string Schedule => IsCustom ? Custom : IsIncreasing ? $"Exponential({Growth}, {Interval})" : $"Constant({Interval})";
    public string Error
    {
        get { try { SgdReporting.parse(Schedule); return ""; } catch (Exception e) { return e.GetBaseException().Message; } }
    }
    public string Preview => Error.Length > 0 ? "" : "Sample generations: "
        + string.Join(", ", SgdReporting.samples(Schedule, int.MaxValue).Take(8))
        + " …";
    private void Notify() { OnPropertyChanged(""); changed(); }
    internal void Load(string schedule)
    {
        custom = schedule;
        try
        {
            switch (SgdReporting.parse(schedule))
            {
                case intSampleMethod.Constant constant:
                    mode = Modes[0]; interval = constant.stepSize.ToString(CultureInfo.InvariantCulture); break;
                case intSampleMethod.Exponential exponential:
                    mode = Modes[1]; interval = exponential.firstDelta.ToString(CultureInfo.InvariantCulture);
                    growth = exponential.growthRate.ToString("R", CultureInfo.InvariantCulture); break;
                default: mode = Modes[2]; break;
            }
        }
        catch { mode = Modes[2]; }
        OnPropertyChanged("");
    }
}
