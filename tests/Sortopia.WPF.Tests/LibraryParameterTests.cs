using System.Globalization;
using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class LibraryParameterTests
{
    [Fact]
    public void Library_ids_remain_typed_through_bindings_serialization_and_parameter_sets()
    {
        var sorter = sorterLibId.create(16, sorterLibVariant.VariantA);
        var prefix = prefixLibId.create(16, 1, prefixLibVariant.Prefix1);
        var merge = mergeLibId.create(16, 2, sorterLibVariant.VariantA);
        parameterBinding[] bindings = [ParameterBinding.create(ParamKeys.sorterLibIdKey, sorter),
            ParameterBinding.create(ParamKeys.prefixLibIdKey, prefix), ParameterBinding.create(ParamKeys.mergeLibIdKey, merge),
            ParameterBinding.create(ParamKey.prefixLibraryId("CustomPrefix"), prefix)];
        foreach (var binding in bindings)
        {
            Assert.NotEqual(parameterValueType.Text, binding.ValueType);
            Assert.Equal(binding, ParameterBindingDto.toDomain(MessagePackSetup.deserialize<parameterBindingDto>(MessagePackSetup.serialize(ParameterBindingDto.fromDomain(binding)))));
            Assert.Equal(binding.Value, ParameterValue.parse(binding.ValueType, binding.FormattedValue));
        }
        var parameters = parameterSet.fromBindings(bindings);
        Assert.Same(sorter, parameters.TryFind(ParamKeys.sorterLibIdKey).Value);
        Assert.Same(prefix, parameters.TryFind(ParamKeys.prefixLibIdKey).Value);
        Assert.Same(merge, parameters.TryFind(ParamKeys.mergeLibIdKey).Value);
        Assert.Null(parameters.TryFind(ParamKey.text("MergeLibId")));
        var spans = ListModule.OfArray(new[] { ParameterSpan.create(ParamKeys.mergeLibIdKey, ListModule.OfArray(new[] { merge, mergeLibId.create(16, 4, sorterLibVariant.VariantB) })) });
        var sets = ParameterSets.create(ListModule.OfSeq(bindings.Where(binding => binding.KeyName != "MergeLibId")), spans);
        Assert.Equal(2, sets.Length);
        Assert.Equal(2, sets[0].TryFind(ParamKeys.mergeLibIdKey).Value.MergeDimension);
        Assert.Equal(4, sets[1].TryFind(ParamKeys.mergeLibIdKey).Value.MergeDimension);
        Assert.All(sets, set => Assert.IsType<parameterValue.PrefixLibIdValue>(set.TryFind("CustomPrefix").Value.Value));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromText("SorterLibId", parameterValueType.Text, SorterLibId.toString(sorter)));
        Assert.Throws<ArgumentNullException>(() => ParameterBinding.create<mergeLibId>(ParamKeys.mergeLibIdKey, null!));
        Assert.Throws<ArgumentNullException>(() => sorterLibId.create(4, null!));
    }

    [Theory]
    [InlineData(parameterValueType.SorterLibId, "SorterLibId(channelCount=0, Variant=VariantA)")]
    [InlineData(parameterValueType.PrefixLibId, "PrefixLibId(channelCount=16, stageLength=0, Variant=Prefix1)")]
    [InlineData(parameterValueType.MergeLibId, "MergeLibId(channelCount=16, mergeDimension=3, Variant=VariantA)")]
    [InlineData(parameterValueType.MergeLibId, "MergeLibId(channelCount=16, mergeDimension=2, Variant=Prefix1)")]
    public void Library_parameter_validation_rejects_invalid_fields(parameterValueType type, string invalid)
    {
        Assert.Null(ParameterValue.tryParse(type, invalid));
        Assert.NotEmpty(ParameterValue.validationError(type, invalid));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromText("CustomLibrary", type, invalid));
    }

    [Fact]
    public void Stable_data_identity_uses_all_library_fields_and_their_type()
    {
        var project = Guid.NewGuid();
        Guid Identity(parameterBinding value) => DataKey.create(project, "Results", false, "f", "1", sortopiaDataType.NewTextReport(""),
            ListModule.OfArray(new[] { "Custom" }), parameterSet.fromBindings([value]), ListModule.Empty<Sortopia.Functions.storedDataReference>()).Id;
        var first = ParameterBinding.create(ParamKey.mergeLibraryId("Custom"), mergeLibId.create(16, 2, sorterLibVariant.VariantA));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var parsed = ParameterBinding.fromText("Custom", parameterValueType.MergeLibId, "  MergeLibId(channelCount=0016, mergeDimension=02, Variant=VariantA)  ");
            Assert.Equal(Identity(first), Identity(parsed));
            foreach (var other in new[] { mergeLibId.create(32, 2, sorterLibVariant.VariantA), mergeLibId.create(16, 4, sorterLibVariant.VariantA), mergeLibId.create(16, 2, sorterLibVariant.VariantB) })
                Assert.NotEqual(Identity(first), Identity(ParameterBinding.create(ParamKey.mergeLibraryId("Custom"), other)));
            Assert.NotEqual(Identity(first), Identity(ParameterBinding.create(ParamKey.text("Custom"), first.FormattedValue)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(parameterValueType.SorterLibId)]
    [InlineData(parameterValueType.PrefixLibId)]
    [InlineData(parameterValueType.MergeLibId)]
    public void Custom_library_keys_use_structured_editors_and_keep_invalid_field_drafts(parameterValueType type)
    {
        var row = new WorkspaceParameterBinding("CustomLibrary", type, "retained input", _ => { });
        Assert.True(row.Value.IsLibraryId); Assert.Equal("retained input", row.Value.Text);
        var fields = row.Value.LibraryId;
        fields.ChannelCount = "16";
        if (fields.IsPrefix) fields.StageLength = "1";
        if (fields.IsMerge) fields.MergeDimension = "2";
        fields.Variant = fields.IsPrefix ? "Prefix1" : "VariantA";
        Assert.Empty(row.Error);
        Assert.Equal(type, ParameterBinding.fromText(row.Key, row.ValueType, row.Value.Text).ValueType);
        fields.ChannelCount = "bad";
        Assert.Contains("Channel count", row.Error); Assert.Equal("bad", fields.ChannelCount);
        fields.ChannelCount = "16"; Assert.Empty(row.Error);
        if (fields.IsMerge)
        {
            fields.MergeDimension = "3"; Assert.Contains("divide", row.Error);
            fields.MergeDimension = "2"; Assert.Empty(row.Error);
        }
        if (fields.IsPrefix)
        {
            fields.StageLength = "0"; Assert.Contains("Stage length", row.Error);
            fields.StageLength = "1"; Assert.Empty(row.Error);
        }
        var id = fields.CatalogChoices.First(); fields.SelectedCatalogId = id;
        Assert.Equal(id, row.Value.Text); Assert.Empty(row.Error);
        row.ValueType = parameterValueType.Text;
        Assert.True(row.Value.IsTextEditor); Assert.Equal(id, row.Value.Text);
        row.ValueType = type; Assert.True(row.Value.IsLibraryId); Assert.Empty(row.Error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Inherited_function_keys_keep_library_types_through_editing_and_saved_runs(bool branch, bool onProject)
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-library-parameters-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(root);
            var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); var ancestor = page.Selected!;
            ancestor.Editor!.LoadProject(RunExecutionTests.Settings(branch));
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
            if (onProject) page.AddProjectCommand.Execute(null); var editor = page.Selected!.Editor!;
            editor.AddResultsParameterDefaultCommand.Execute(null); var local = editor.ResultsParameterDefaults.Last();
            Assert.Contains("MergeLibId", local.KeyChoices); Assert.DoesNotContain("CustomSorter", local.KeyChoices);
            local.Key = "MergeLibId"; Assert.Equal(parameterValueType.MergeLibId, local.ValueType);
            local.Value.LibraryId.SelectedCatalogId = MergeLibId.toString(MergeLibId.availableForChannelCount(4).First(id => id.MergeDimension == 2));
            Assert.Empty(local.Error); if (!onProject) page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
            var run = page.Selected!; WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false); if (branch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
            Assert.True(run.Editor!.ResultsParameters.Single(row => row.Key == "MergeLibId").Values[0].IsLibraryId);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            var savedRun = WorkspaceFileStore.LoadDefinition(root).Modules[0].Modules[0].Projects[0].Runs[0];
            Assert.IsType<parameterValue.MergeLibIdValue>(RunParameterSets.create(savedRun).Results[0].TryFind("MergeLibId").Value.Value);
        }
        finally { temporary.Delete(true); }
    }
}
