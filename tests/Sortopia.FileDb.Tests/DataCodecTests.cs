using MessagePack;
using Sortopia.Core.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class DataCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(1048576)]
    public void Length_without_payload_copy_matches_actual_wire_bytes_at_binary_header_boundaries(int length)
    {
        foreach (var kind in new[] { "SorterPoolSet", new string('x', 32), "Résumé" })
        {
            var dto = new sortopiaStorageDataDto(2, kind, new byte[length]);
            Assert.Equal(MessagePackSetup.serialize(dto).LongLength, DataCodec.payloadLength(dto));
        }
    }

    [Fact]
    public void Decode_still_rejects_incorrect_declared_length_before_reading_inner_data()
    {
        var dto = new sortopiaStorageDataDto(2, "SorterPoolSet", [1, 2, 3]);
        var bytes = MessagePackSerializer.Serialize(dto, DataCodec.compressedOptions, TestContext.Current.CancellationToken);
        var error = Assert.Throws<InvalidOperationException>(() =>
            DataCodec.decode(dto.Kind, (int)DataCodec.payloadLength(dto) + 1, bytes));
        Assert.Contains("length does not match", error.Message);
    }
}
