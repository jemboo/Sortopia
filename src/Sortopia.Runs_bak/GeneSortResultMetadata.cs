using MessagePack;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;

namespace Sortopia.Runs;

// GeneSort writes a tagged tests union followed by its ordered prefix. Its wire
// records use string keys. Inventorying the file does not require unpacking SIMD lanes.
internal sealed record GeneSortResultMetadata(Guid Id, string Format, int Channels, long Count,
    int PrefixCeCount, prefixLibId? CatalogPrefix)
{
    internal static GeneSortResultMetadata Read(string path)
    {
        var reader = new MessagePackReader(File.ReadAllBytes(path));
        if (reader.ReadArrayHeader() != 2 || reader.ReadArrayHeader() != 2)
            throw new InvalidDataException("Expected GeneSort SortableTestsWithPrefix.");
        var tag = reader.ReadInt32();
        var format = tag switch
        {
            0 => "IntArray", 1 => "BoolArray", 2 => "Int8Vector256", 3 => "Int8Vector512", 4 => "BitVector512",
            _ => throw new InvalidDataException($"Unknown GeneSort sortable tests tag {tag}.")
        };
        if (reader.ReadArrayHeader() != 1) throw new InvalidDataException("Expected one test dataset.");

        Guid id = Guid.Empty;
        int channels = 0;
        long count = -1;
        var fields = reader.ReadMapHeader();
        for (var i = 0; i < fields; i++)
        {
            switch (reader.ReadString())
            {
                case "Id": id = Guid.Parse(reader.ReadString()!); break;
                case "ChannelCount": channels = reader.ReadInt32(); break;
                case "SortableArrays" when tag < 2:
                    var arrays = reader.ReadArrayHeader();
                    for (var j = 0; j < arrays; j++) reader.Skip();
                    count = arrays;
                    break;
                case "Blocks" when tag >= 2:
                    count = CountBlocks(ref reader, tag switch { 2 => 32, 3 => 64, _ => 512 });
                    break;
                default: reader.Skip(); break;
            }
        }
        if (channels <= 0 || count < 0 || id == Guid.Empty ||
            !Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var fileId) || fileId != id)
            throw new InvalidDataException("Missing dataset metadata or the dataset ID differs from the filename.");

        int length = reader.ReadArrayHeader();
        if (length > reader.Sequence.Length - reader.Consumed) throw new InvalidDataException("Incomplete CE prefix.");
        var prefix = new int[length];
        for (int i = 0; i < length; i++)
        {
            var index = ReadIntField(ref reader, "Index");
            if (index < 0 || index >= (long)channels * (channels + 1L) / 2)
                throw new InvalidDataException("CE index outside the dataset width.");
            prefix[i] = index;
        }
        if (!reader.End) throw new InvalidDataException("Unexpected trailing Result data.");

        var catalogPrefix = PrefixLib.ids().Where(key => key.ChannelCount == channels)
            .FirstOrDefault(key => SorterDataParse.getCeArrayFromPrefixLib(key).Value.Select(Ce.toIndex).SequenceEqual(prefix));
        return new(id, format, channels, count, length, catalogPrefix);
    }

    private static long CountBlocks(ref MessagePackReader reader, int capacity)
    {
        long total = 0;
        var blocks = reader.ReadArrayHeader();
        for (int i = 0; i < blocks; i++)
        {
            var count = ReadIntField(ref reader, "SortableCount");
            if (count < 0 || count > capacity) throw new InvalidDataException("Invalid SIMD block count.");
            total += count;
        }
        return total;
    }

    private static int ReadIntField(ref MessagePackReader reader, string name)
    {
        int? value = null;
        var fields = reader.ReadMapHeader();
        for (int i = 0; i < fields; i++)
            if (reader.ReadString() == name) value = reader.ReadInt32();
            else reader.Skip();
        return value ?? throw new InvalidDataException($"Missing {name}.");
    }
}
