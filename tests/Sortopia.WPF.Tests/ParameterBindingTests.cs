using System.Globalization;
using System.Text.Json.Nodes;
using MessagePack;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ParameterBindingTests
{
    [Fact]
    public void Bindings_support_all_value_types_without_a_project_or_default_context()
    {
        var id = Guid.NewGuid();
        parameterBinding[] bindings =
        [
            ParameterBinding.create(ParamKeys.channelCountKey, 8),
            ParameterBinding.create(ParamKeys.seedSossKey, ulong.MaxValue),
            ParameterBinding.create(ParamKeys.activationProbabilityKey, .25),
            ParameterBinding.create(ParamKeys.setupCompletedKey, true),
            ParameterBinding.create(ParamKeys.idKey, id),
            ParameterBinding.create(ParamKeys.messageKey, " α, text\n ")
        ];
        foreach (var binding in bindings)
        {
            Assert.Equal(binding, ParameterBinding.fromValue(binding.KeyName, binding.Value));
            Assert.Equal(binding, ParameterBinding.fromText(binding.KeyName, binding.ValueType, binding.FormattedValue));
            ParameterBinding.validate(binding);
        }
        var parameters = parameterSet.fromBindings(bindings);
        foreach (var binding in bindings)
        {
            Assert.Same(binding, parameters.ParamMap[binding.KeyName]);
            Assert.Same(binding, parameters.TryFind(binding.KeyName).Value);
        }
        Assert.Equal(8, parameters.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Equal(ulong.MaxValue, parameters.TryFind(ParamKeys.seedSossKey).Value);
        Assert.Equal(.25, parameters.TryFind(ParamKeys.activationProbabilityKey).Value);
        Assert.True(parameters.TryFind(ParamKeys.setupCompletedKey).Value);
        Assert.Equal(id, parameters.TryFind(ParamKeys.idKey).Value);
        Assert.Equal(" α, text\n ", parameters.TryFind(ParamKeys.messageKey).Value);
    }

    [Fact]
    public void Parameter_sets_accept_general_binding_sequences_and_immutable_updates()
    {
        var enumerations = 0;
        IEnumerable<parameterBinding> Bindings()
        {
            Assert.Equal(1, ++enumerations);
            yield return ParameterBinding.fromValue("ChannelCount", parameterValue.NewIntegerValue(8));
            yield return ParameterBinding.fromValue("Message", parameterValue.NewTextValue("keep"));
        }
        var original = parameterSet.fromBindings(Bindings());
        var updated = original.WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 16))
            .WithBinding(ParameterBinding.fromText("Custom", parameterValueType.Boolean, "false"));
        Assert.Equal(8, original.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Null(original.TryFind("Custom"));
        Assert.Equal(16, updated.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Equal("keep", updated.TryFind(ParamKeys.messageKey).Value);
        Assert.False(updated.TryFind(ParamKey.boolean("Custom")).Value);
        Assert.Empty(parameterSet.fromBindings([]).ParamMap);
    }

    [Fact]
    public void Bindings_validate_typed_values_and_reject_ambiguous_collections()
    {
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromValue("ChannelCount", parameterValue.NewTextValue("8")));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromValue("", parameterValue.NewIntegerValue(8)));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromValue("Custom", parameterValue.NewFloatingValue(double.NaN)));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromValue("Custom", parameterValue.NewFloatingValue(double.PositiveInfinity)));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromValue("Custom", parameterValue.NewTextValue(null!)));
        Assert.Throws<ArgumentNullException>(() => ParameterBinding.fromValue("Custom", null!));
        Assert.Throws<ArgumentNullException>(() => ParameterBinding.create<int>(null!, 1));
        Assert.Throws<ArgumentNullException>(() => ParameterBinding.validate(null!));
        Assert.Throws<ArgumentNullException>(() => ParameterBinding.validateUnique(null!));
        Assert.Throws<ArgumentNullException>(() => parameterSet.fromBindings(null!));
        Assert.Throws<ArgumentNullException>(() => parameterSet.fromBindings([null!]));
        Assert.Throws<ArgumentNullException>(() => parameterSet.fromBindings([]).WithBinding(null!));
        parameterBinding[] duplicates = [ParameterBinding.fromValue("Custom", parameterValue.NewIntegerValue(1)), ParameterBinding.fromValue("Custom", parameterValue.NewIntegerValue(2))];
        Assert.Throws<ArgumentException>(() => ParameterBinding.validateUnique(duplicates));
        Assert.Throws<ArgumentException>(() => parameterSet.fromBindings(duplicates));
    }

    [Fact]
    public void Generic_binding_dto_preserves_the_existing_wire_fields_and_canonical_values()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var binding = ParameterBinding.fromText("Custom", parameterValueType.Floating, "2.5e-1");
            var bytes = MessagePackSetup.serialize(ParameterBindingDto.fromDomain(binding));
            var json = JsonNode.Parse(MessagePackSerializer.ConvertToJson(bytes, cancellationToken: TestContext.Current.CancellationToken))!.AsObject();
            Assert.Equal(new[] { "keyName", "value", "valueType" }, json.Select(pair => pair.Key).Order(StringComparer.Ordinal));
            Assert.Equal("Custom", json["keyName"]!.GetValue<string>());
            Assert.Equal("0.25", json["value"]!.GetValue<string>());
            Assert.Equal(binding, ParameterBindingDto.toDomain(MessagePackSetup.deserialize<parameterBindingDto>(bytes)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.Throws<ArgumentNullException>(() => ParameterBindingDto.fromDomain(null!));
        Assert.Throws<ArgumentNullException>(() => ParameterBindingDto.toDomain(null!));
    }
}
