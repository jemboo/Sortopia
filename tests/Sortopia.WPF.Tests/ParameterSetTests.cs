using System.Globalization;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ParameterSetTests
{
    [Fact]
    public void Typed_construction_updates_and_reads_do_not_call_string_converters()
    {
        var key = new paramKey<int>("CustomInteger",
            FuncConvert.FromFunc<string, FSharpOption<int>>(_ => throw new InvalidOperationException("Unexpected parse")),
            FuncConvert.FromFunc<int, string>(_ => throw new InvalidOperationException("Unexpected format")));
        var binding = ParameterBinding.create(key, 7);
        var original = parameterSet.fromBindings([binding]);
        var updated = original.WithValue(key, FSharpOption<int>.Some(9));
        Assert.Same(binding, original.TryFind(key.Name).Value);
        Assert.Equal(7, original.TryFind(key).Value);
        Assert.Equal(9, updated.TryFind(key).Value);
        Assert.Equal(parameterValue.NewIntegerValue(9), updated.ParamMap[key.Name].Value);
        Assert.Null(updated.TryFind(ParamKey.text(key.Name)));
        Assert.Null(updated.TryFind(ParamKey.floating(key.Name)));
        Assert.Null(updated.TryFind(ParamKey.integer("Missing")));
        Assert.Equal("9", updated.TryFindText(key.Name).Value);
        Assert.Equal("9", updated.ToStringMap()[key.Name]);
        var removed = updated.WithValue(key, FSharpOption<int>.None);
        Assert.Null(removed.TryFind(key));
        Assert.Equal(9, updated.TryFind(key).Value);
    }

    [Fact]
    public void Name_based_updates_preserve_typed_values_and_reject_known_key_mismatches()
    {
        var original = parameterSet.fromBindings([ParameterBinding.fromValue("Custom", parameterValue.NewTextValue("8"))]);
        Assert.Null(original.TryFind(ParamKey.integer("Custom"))); // Numeric-looking text stays text.
        Assert.Equal("8", original.TryFind(ParamKey.text("Custom")).Value);
        var value = parameterValue.NewIntegerValue(8);
        var updated = original.WithValue("Custom", FSharpOption<parameterValue>.Some(value));
        Assert.Same(value, updated.TryFind("Custom").Value.Value);
        Assert.Equal(8, updated.TryFind(ParamKey.integer("Custom")).Value);
        Assert.Null(updated.TryFind(ParamKey.text("Custom")));
        Assert.Null(updated.WithValue("Custom", FSharpOption<parameterValue>.None).TryFind("Custom"));
        Assert.Throws<ArgumentException>(() => updated.WithValue("ChannelCount", FSharpOption<parameterValue>.Some(parameterValue.NewTextValue("8"))));
        Assert.Throws<ArgumentNullException>(() => updated.WithValue("Custom", FSharpOption<parameterValue>.Some(null!)));
        Assert.Throws<ArgumentException>(() => updated.WithValue(" ", FSharpOption<parameterValue>.None));
        Assert.Throws<ArgumentNullException>(() => updated.WithValue<int>(null!, FSharpOption<int>.None));
        Assert.Throws<ArgumentNullException>(() => updated.TryFind<int>(null!));
    }

    [Fact]
    public void Typed_map_constructor_requires_matching_binding_keys()
    {
        var binding = ParameterBinding.create(ParamKeys.channelCountKey, 8);
        var map = MapModule.OfSeq(new[] { Tuple.Create(binding.KeyName, binding) });
        var parameters = parameterSet.create(map);
        Assert.Same(binding, parameters.ParamMap[binding.KeyName]);
        Assert.Throws<ArgumentException>(() => parameterSet.create(MapModule.OfSeq(new[] { Tuple.Create("WrongKey", binding) })));
        Assert.Throws<ArgumentNullException>(() => parameterSet.create(MapModule.OfSeq(new[] { Tuple.Create("ChannelCount", (parameterBinding)null!) })));
        Assert.Throws<ArgumentNullException>(() => parameterSet.create(null!));
    }

    [Fact]
    public void Span_expansion_builds_typed_bindings_and_keeps_fixed_bindings()
    {
        var id = Guid.NewGuid();
        var fixedBinding = ParameterBinding.fromValue("Fixed", parameterValue.NewUnsigned64Value(42));
        var bindings = ListModule.OfArray(new[] { fixedBinding, ParameterBinding.fromValue("CustomInteger", parameterValue.NewIntegerValue(1)) });
        var spans = ListModule.OfArray(new[]
        {
            ParameterSpan.create(ParamKey.integer("CustomInteger"), ListModule.OfArray(new[] { 2, 3 })),
            ParameterSpan.create(ParamKeys.seedSossKey, ListModule.OfArray(new[] { ulong.MaxValue })),
            ParameterSpan.create(ParamKeys.activationProbabilityKey, ListModule.OfArray(new[] { .125 })),
            ParameterSpan.create(ParamKeys.setupCompletedKey, ListModule.OfArray(new[] { false })),
            ParameterSpan.create(ParamKeys.idKey, ListModule.OfArray(new[] { id })),
            ParameterSpan.create(ParamKeys.messageKey, ListModule.OfArray(new[] { " α, text\n " }))
        });
        var sets = ParameterSets.create(bindings, spans);
        Assert.Equal(new[] { 2, 3 }, sets.Select(set => set.TryFind(ParamKey.integer("CustomInteger")).Value));
        Assert.All(sets, set =>
        {
            Assert.Same(fixedBinding, set.ParamMap["Fixed"]);
            Assert.Equal(parameterValue.NewUnsigned64Value(ulong.MaxValue), set.ParamMap["SeedSoss"].Value);
            Assert.Equal(parameterValue.NewFloatingValue(.125), set.ParamMap["ActivationProbability"].Value);
            Assert.Equal(parameterValue.NewBooleanValue(false), set.ParamMap["SetupCompleted"].Value);
            Assert.Equal(parameterValue.NewGuidValue(id), set.ParamMap["Id"].Value);
            Assert.Equal(parameterValue.NewTextValue(" α, text\n "), set.ParamMap["Message"].Value);
        });
    }

    [Fact]
    public void Explicit_text_projections_are_invariant_and_do_not_change_stored_values()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var binding = ParameterBinding.fromValue("Custom", parameterValue.NewFloatingValue(.25));
            var parameters = parameterSet.fromBindings([binding, ParameterBinding.create(ParamKeys.messageKey, "")]);
            Assert.Equal("0.25", parameters.TryFindText("Custom").Value);
            Assert.Equal("0.25", parameters.ToStringMap()["Custom"]);
            Assert.Equal("", parameters.TryFindText("Message").Value);
            Assert.Null(parameters.TryFindText("Missing"));
            Assert.Same(binding, parameters.ParamMap["Custom"]);
            Assert.Equal(.25, parameters.TryFind(ParamKey.floating("Custom")).Value);
            Assert.NotEqual(parameters, parameterSet.fromBindings([ParameterBinding.fromValue("Custom", parameterValue.NewTextValue("0.25")), ParameterBinding.create(ParamKeys.messageKey, "")]));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
