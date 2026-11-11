using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceDataTypeTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-data-types-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static readonly (string Kind, Func<string, sortopiaDataType> Create)[] Cases =
    [
        ("Workspace", sortopiaDataType.NewWorkspace), ("Run", sortopiaDataType.NewRun),
        ("WorkspaceModule", sortopiaDataType.NewWorkspaceModule), ("RunParameters", sortopiaDataType.NewRunParameters),
        ("SorterPoolSet", sortopiaDataType.NewSorterPoolSet), ("SorterSet", sortopiaDataType.NewSorterSet),
        ("SorterModelSet", sortopiaDataType.NewSorterModelSet), ("SortableTests", sortopiaDataType.NewSortableTests),
        ("SorterSetEval", sortopiaDataType.NewSorterSetEval), ("TextReport", sortopiaDataType.NewTextReport)
    ];
    [Fact]
    public void Every_case_preserves_free_form_text_in_MessagePack()
    {
        Assert.Equal(Cases.Select(c => c.Kind), SortopiaDataType.kinds);
        foreach (var c in Cases) foreach (var text in new[] { "", "   ", " α, \"quoted\"\r\nsecond line\t\0 " })
        {
            var value = c.Create(text);
            var dto = SortopiaDataTypeDto.fromDomain(value);
            Assert.Equal(value, SortopiaDataTypeDto.toDomain(MessagePackSetup.deserialize<sortopiaDataTypeDto>(MessagePackSetup.serialize(dto))));
        }
    }
    [Fact]
    public void Boundaries_reject_unknown_cases_and_nulls()
    {
        Assert.Throws<ArgumentException>(() => SortopiaDataTypeDto.toDomain(new() { kind = "Unknown", value = "" }));
        Assert.Throws<ArgumentException>(() => SortopiaDataTypeDto.toDomain(new() { kind = "run", value = "" }));
        Assert.Throws<ArgumentNullException>(() => SortopiaDataTypeDto.toDomain(new() { kind = "Run", value = null! }));
        Assert.Throws<ArgumentNullException>(() => SortopiaDataTypeDto.toDomain(null!));
    }
    [Fact]
    public void Catalog_outputs_cannot_be_replaced_by_project_overrides()
    {
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsSortopiaDataTypes"));
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsQueryParameterKeys"));
        Assert.Null(typeof(projectOverridesDto).GetProperty("setupDataTypes"));
        var value = RunExecutionTests.MakeRun(false);
        Assert.Equal(new[] { sortopiaDataType.NewSortableTests("") }, value.ResultsSortopiaDataTypes);
        var dto = SettingsDto.fromDomain(projectConfiguration.NewRootProjectConfig(((run.RootRun)value).Item.ProjectConfiguration));
        dto.resultsDataTypes[0].dataType.value = "altered";
        var config = (projectConfiguration.RootProjectConfig)SettingsDto.toDomain(Sortopia.Workspaces.hierarchyKind.Root, dto);
        Assert.Throws<ArgumentException>(() => Sortopia.Functions.FunctionConfiguration.validateResults(config.Item.Results));
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
