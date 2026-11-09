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
    private static project Parent(bool branch, sortopiaDataType[] values)
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsSortopiaDataTypes = values.Select(SortopiaDataTypeDto.fromDomain).ToArray();
        settings.setupDataTypes = branch ? values.Reverse().Select(value => WorkspaceHierarchyTests.SetupType(SortopiaDataType.kindName(value), SortopiaDataType.text(value), "Width")).ToArray() : [];
        return project.create(Guid.NewGuid(), "P", "P", SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_case_preserves_free_form_text_in_project_and_run_round_trips(bool branch)
    {
        Assert.Equal(Cases.Select(c => c.Kind), SortopiaDataType.kinds);
        Assert.Equal(Cases.Select(c => c.Kind), WorkspaceDataTypeEntry.Kinds);
        string[] texts = ["", "   ", " α, \"quoted\"\r\nsecond line\t\0 "];
        var values = Cases.SelectMany(c => texts.Select(text =>
        {
            var value = c.Create(text);
            Assert.Equal(value, SortopiaDataType.create(c.Kind, text));
            Assert.Equal(c.Kind, SortopiaDataType.kindName(value));
            Assert.Equal(text, SortopiaDataType.text(value));
            return value;
        })).ToArray();
        var parent = Parent(branch, values);
        var inputs = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", branch));
        inputs.parentProjectId = parent.Id;
        if (branch) inputs.setupParameterSpans = inputs.setupParameterSpans.Concat(inputs.resultsParameterSpans).ToArray();
        var run = RunDto.toDomain(parent, inputs);
        parent = project.create(parent.Id, parent.ProjectName, parent.FolderName, parent.Configuration, [run]);
        var module = workspaceModule.create(parent.Kind, Guid.NewGuid(), "M", "M", [], [parent]);
        var source = Parent(false, values);
        source = project.restore(WorkspaceTestFiles.Id("source"), "Source", "Source", source.Configuration, source.LocalOverrides, true, []);
        var sources = workspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Sources", "Sources", [], [source]);
        var workspace = Sortopia.Workspaces.workspace.create(Guid.NewGuid(), "W", "W", [module, sources]);
        var restored = WorkspaceCodec.decodeWorkspace(WorkspaceCodec.encodeWorkspace(workspace)).Modules[0].Projects[0];
        Assert.Equal(values, restored.ResultsSortopiaDataTypes);
        Assert.Equal(values, restored.Runs[0].ResultsSortopiaDataTypes);
        Assert.Equal(values, WorkspaceCodec.decodeProject(WorkspaceCodec.encodeProject(parent)).ResultsSortopiaDataTypes);
        if (branch) Assert.Equal(values.Reverse(), restored.Runs[0].SetupSortopiaDataTypes.Value);
        Assert.Equal(9, MessagePackSetup.deserialize<workspaceDto>(WorkspaceCodec.encodeWorkspace(workspace)).schemaVersion);
    }

    [Fact]
    public void Queries_include_both_case_and_exact_text_in_membership_and_identity()
    {
        sortopiaDataType[] values = [sortopiaDataType.NewTextReport("notes"), sortopiaDataType.NewTextReport("notes\n"), sortopiaDataType.NewSortableTests("notes")];
        var parent = Parent(true, values);
        var inputs = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", true));
        inputs.parentProjectId = parent.Id;
        var run = RunDto.toDomain(parent, inputs);
        var branch = ((Sortopia.Runs.run.BranchRun)run).Item;
        var parameters = BranchRun.makeResultsParameterSets(branch)[0];
        var queries = values.Select(value => QueryParams.forResults(run, value, parameters)).ToArray();
        Assert.Equal(values.Length, queries.Select(q => q.Id).Distinct().Count());
        Assert.Equal(values, queries.Select(q => q.OutputDataType));
        Assert.Equal(queries[0].Id, QueryParams.forResults(run, sortopiaDataType.NewTextReport("notes"), parameters).Id);
        var setupParameters = BranchRun.makeSetupParameterSets(branch)[0];
        Assert.Equal(values.Length, values.Select(value => QueryParams.forSetup(branch, value, setupParameters).Id).Distinct().Count());
        Assert.Throws<ArgumentException>(() => QueryParams.forResults(run, sortopiaDataType.NewTextReport("notes "), parameters));
        Assert.Throws<ArgumentException>(() => QueryParams.forSetup(branch, sortopiaDataType.NewRun("notes"), setupParameters));
    }

    [Fact]
    public void Boundaries_reject_unknown_cases_nulls_and_duplicate_pairs()
    {
        Assert.Throws<ArgumentException>(() => SortopiaDataTypeDto.toDomain(new() { kind = "Unknown", value = "" }));
        Assert.Throws<ArgumentException>(() => SortopiaDataTypeDto.toDomain(new() { kind = "run", value = "" }));
        Assert.Throws<ArgumentNullException>(() => SortopiaDataTypeDto.toDomain(new() { kind = "Run", value = null! }));
        Assert.Throws<ArgumentNullException>(() => SortopiaDataTypeDto.toDomain(null!));
        Assert.Throws<ArgumentNullException>(() => Parent(false, [null!]));
        Assert.Throws<ArgumentNullException>(() => Parent(false, [sortopiaDataType.NewRun(null!)]));
        Assert.Throws<ArgumentException>(() => Parent(false, [sortopiaDataType.NewRun(""), sortopiaDataType.NewRun("")]));
        var parent = Parent(true, [sortopiaDataType.NewRun("")]);
        var malformed = ProjectDto.fromDomain(parent);
        malformed.localOverrides.setupDataTypes = [WorkspaceHierarchyTests.SetupType("invalid")];
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeProject(MessagePackSetup.serialize(malformed)));
        malformed = ProjectDto.fromDomain(parent);
        malformed.schemaVersion = 7;
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeProject(MessagePackSetup.serialize(malformed)));
    }

    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editors_inherit_override_and_reopen_case_text_pairs_without_losing_order(bool branch)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var module = page.Selected!;
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsQueryParameterKeys = [];
        module.Editor!.LoadProject(settings);
        module.Editor.OutputTypes[0].Kind = "WorkspaceModule";
        module.Editor.OutputTypes[0].Text = " α, inherited\ntext ";
        if (branch) { WorkspaceHierarchyTests.AddSetupSource(module, " setup only "); module.Editor.SetupDataTypes[0].QueryKeys.Clear(); }
        page.AddProjectCommand.Execute(null);
        var project = page.Selected!;
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        Assert.Same(module.Editor, run.Editor!.OutputTypesOverride.InheritedSource);
        Assert.Equal(" α, inherited\ntext ", Assert.Single(run.Editor.InheritedOutputTypes).value);
        project.Editor!.OutputTypesOverride.IsOverridden = true;
        Assert.Equal(" α, inherited\ntext ", Assert.Single(project.Editor.OutputTypes).Text);
        project.Editor.OutputTypes[0].Text = "local";
        project.Editor.AddOutputTypeCommand.Execute(null);
        project.Editor.OutputTypes[1].Kind = "WorkspaceModule";
        project.Editor.OutputTypes[1].Text = "local";
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Contains("unique", project.Editor.OutputTypesError);
        project.Editor.OutputTypes[1].Text = "";
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Equal(new[] { "local", "" }, run.Editor.InheritedOutputTypes.Select(v => v.value));
        Assert.Same(project.Editor, run.Editor.OutputTypesOverride.InheritedSource);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var stored = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects.Last().Runs[0];
        Assert.Equal(new[] { sortopiaDataType.NewWorkspaceModule("local"), sortopiaDataType.NewWorkspaceModule("") }, stored.ResultsSortopiaDataTypes);
        if (branch) Assert.Equal(" setup only ", SortopiaDataType.text(Assert.Single(stored.SetupSortopiaDataTypes.Value)));
        var reopened = Open();
        var reopenedModule = reopened.Roots[0].Children[0];
        var reopenedProject = reopenedModule.Children.Last();
        var reopenedRun = reopenedProject.Children[0];
        Assert.Equal(new[] { "local", "" }, reopenedProject.Editor!.OutputTypes.Select(v => v.Text));
        reopenedProject.Editor.OutputTypesOverride.IsOverridden = false;
        Assert.Equal(new[] { "local", "" }, reopenedRun.Editor!.InheritedOutputTypes.Select(value => value.value));
        reopenedModule.Editor!.OutputTypes[0].Text = "updated ancestor";
        Assert.Equal(new[] { "local", "" }, reopenedRun.Editor.InheritedOutputTypes.Select(value => value.value));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
