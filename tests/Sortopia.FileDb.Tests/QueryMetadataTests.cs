using System.Security.Cryptography;
using MessagePack;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Functions;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class QueryMetadataTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-query-metadata-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private dataKey Key(params parameterBinding[] bindings) => DataKey.create(Guid.NewGuid(), "Setup", true, "copy", "1",
        sortopiaDataType.NewSortableTests("catalog tests"), ListModule.OfSeq(bindings.Select(binding => binding.KeyName)),
        parameterSet.fromBindings(bindings.Append(ParameterBinding.fromText("Unselected", parameterValueType.Text, "not a query key"))), []);
    private SortopiaFileDb Write(dataKey key)
    {
        var store = new SortopiaFileDb(temporary.FullName, key.Reference.ProjectId);
        var path = store.GetPath(key.Reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Deliberately not a decodable payload: query display must only read its header.
        byte[] payload = [1, 2, 3];
        var envelope = new dataEnvelopeDto(1, key.Reference.ProjectId, key.Id, key.QueryId, true,
            "SortableTests", "catalog tests", key.QueryBytes, key.IdentityBytes, "messagepack-lz4-array/1", 10, SHA256.HashData(payload), payload);
        File.WriteAllBytes(path, MessagePackSetup.serialize(envelope));
        return store;
    }

    [Theory]
    [InlineData(parameterValueType.Text, "text with spaces")]
    [InlineData(parameterValueType.Integer, "-12")]
    [InlineData(parameterValueType.Unsigned64, "18446744073709551615")]
    [InlineData(parameterValueType.Floating, "0.35")]
    [InlineData(parameterValueType.Boolean, "true")]
    [InlineData(parameterValueType.Guid, "c245f2b9-f8f6-420b-8494-b5379a4ca41d")]
    [InlineData(parameterValueType.SorterLibId, "SorterLibId(channelCount=16, Variant=VariantB)")]
    [InlineData(parameterValueType.PrefixLibId, "PrefixLibId(channelCount=16, stageLength=1, Variant=Prefix1)")]
    [InlineData(parameterValueType.MergeLibId, "MergeLibId(channelCount=16, mergeDimension=2, Variant=VariantA)")]
    [InlineData(parameterValueType.OpsWeights, "1,2,3")]
    [InlineData(parameterValueType.Seed6Weights, "1,1,1,1,1,1,1")]
    [InlineData(parameterValueType.UnfoldingWeights, "1,2,3;3,2,1")]
    [InlineData(parameterValueType.SorterSuccessFraction, "99%")]
    public void Reads_exact_typed_query_values_without_decoding_payloads(parameterValueType type, string text)
    {
        var binding = ParameterBinding.fromText("QueryValue", type, text);
        var key = Key(binding); var store = Write(key);
        var before = File.ReadAllBytes(store.GetPath(key.Reference));
        var saved = Assert.Single(store.ReadQueryParameters(key.Reference, Token));
        Assert.Equal(binding, saved);
        Assert.Equal(binding.FormattedValue, saved.FormattedValue);
        Assert.Equal(before, File.ReadAllBytes(store.GetPath(key.Reference)));
    }

    [Fact]
    public void Empty_projection_has_no_values_even_if_other_parameters_were_supplied()
    {
        var key = Key(); var store = Write(key);
        Assert.Empty(store.ReadQueryParameters(key.Reference, Token));
    }

    [Fact]
    public void Query_display_rejects_wrong_references_corrupt_metadata_and_cancellation()
    {
        var key = Key(ParameterBinding.fromText("QueryValue", parameterValueType.Integer, "16"));
        var store = Write(key);
        var path = store.GetPath(key.Reference);
        var wrongQuery = new storedDataReference(key.Reference.ProjectId, key.Id, Guid.NewGuid(), runExecutionPhase.Setup, key.Reference.DataType);
        Assert.Contains("does not match its reference", Assert.Throws<InvalidOperationException>(() => store.ReadQueryParameters(wrongQuery, Token)).Message);
        var wrongLabel = new storedDataReference(key.Reference.ProjectId, key.Id, key.QueryId, runExecutionPhase.Setup, sortopiaDataType.NewSortableTests("other label"));
        Assert.Throws<InvalidOperationException>(() => store.ReadQueryParameters(wrongLabel, Token));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.ReadQueryParameters(key.Reference, cancelled.Token));
        var envelope = MessagePackSetup.deserialize<dataEnvelopeDto>(File.ReadAllBytes(path));
        envelope.QueryBytes[0] ^= 1;
        File.WriteAllBytes(path, MessagePackSetup.serialize(envelope));
        Assert.Throws<InvalidOperationException>(() => store.ReadQueryParameters(key.Reference, Token));
    }

    public void Dispose() => temporary.Delete(true);
}
