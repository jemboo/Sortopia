using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using MessagePack;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Functions;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class FileInspectionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-data-inspection-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Large_data_summary_reads_the_header_without_loading_or_validating_the_payload()
    {
        var parameters = parameterSet.fromBindings([ParameterBinding.fromText("ChannelCount", parameterValueType.Integer, "16")]);
        var key = DataKey.create(Guid.NewGuid(), "Tests", false, "sortable-tests.standard", "1", sortopiaDataType.NewSortableTests("Binary"),
            ListModule.OfArray(new[] { "ChannelCount" }), parameters, FSharpList<storedDataReference>.Empty);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(13);
        writer.Write(1);
        MessagePackSerializer.Serialize(ref writer, key.Reference.ProjectId, MessagePackSetup.options);
        MessagePackSerializer.Serialize(ref writer, key.Id, MessagePackSetup.options);
        MessagePackSerializer.Serialize(ref writer, key.QueryId, MessagePackSetup.options);
        writer.Write(false); writer.Write("SortableTests"); writer.Write("Binary");
        writer.Write(key.QueryBytes); writer.Write(key.IdentityBytes); writer.Write("messagepack-lz4-array/1");
        writer.Write(256 * 1024 * 1024); writer.Write(new byte[32]);
        writer.WriteBinHeader(128 * 1024 * 1024);
        writer.Flush();
        var path = Path.Combine(temporary.FullName, key.Id + ".msgpack");
        using (var stream = File.Create(path)) { stream.Write(buffer.WrittenSpan); stream.SetLength(buffer.WrittenCount + 128 * 1024 * 1024L); }
        var report = FileInspection.inspectData(path, Token);
        Assert.Equal("Results data", report.Kind);
        Assert.Contains("not decoded or checksum-verified", report.Description);
        using var json = JsonDocument.Parse(report.Json);
        Assert.Equal("Tests", json.RootElement.GetProperty("Database").GetString());
        Assert.Equal(128 * 1024 * 1024, json.RootElement.GetProperty("CompressedPayloadBytes").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("QueryParameterCount").GetInt32());
        using (var stream = File.OpenWrite(path)) stream.SetLength(buffer.WrittenCount);
        Assert.ThrowsAny<Exception>(() => FileInspection.inspectData(path, Token));
    }

    [Fact]
    public void Prepared_parameters_show_types_and_values_and_reject_a_damaged_checksum()
    {
        var dto = new runParametersDto(1, Guid.NewGuid(), Guid.NewGuid(), "sortable-tests.standard", "1", "Results",
            [new preparedOutputDto("SortableTests", "Binary", ["ChannelCount"])],
            [new preparedBindingDto("ChannelCount", (int)parameterValueType.Integer, MessagePackSetup.serialize(16))], [], 1, null!);
        var payload = MessagePackSetup.serialize(dto);
        var id = new Guid(SHA256.HashData(payload)[..16]);
        var options = MessagePackSetup.options.WithCompression(MessagePackCompression.Lz4BlockArray);
        var envelope = new runParametersEnvelopeDto(1, "runParameters", SHA256.HashData(payload), payload);
        var path = Path.Combine(temporary.FullName, id + ".msgpack");
        File.WriteAllBytes(path, MessagePackSerializer.Serialize(envelope, options, Token));
        var report = FileInspection.inspectPrepared(path, Token);
        using var json = JsonDocument.Parse(report.Json);
        var binding = Assert.Single(json.RootElement.GetProperty("Parameters").EnumerateArray());
        Assert.Equal("Integer", binding.GetProperty("Type").GetString());
        Assert.Equal("16", binding.GetProperty("Value").GetString());
        Assert.Contains("checksum", report.Description);
        File.WriteAllBytes(path, MessagePackSerializer.Serialize(new runParametersEnvelopeDto(1, "runParameters", new byte[32], payload), options, Token));
        Assert.ThrowsAny<Exception>(() => FileInspection.inspectPrepared(path, Token));
    }

    public void Dispose() => temporary.Delete(true);
}
