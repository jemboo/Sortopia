using System.IO;
using MessagePack;
using Microsoft.VisualBasic.FileIO;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class GeneSortInventoryTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("Sortopia-inventory-").FullName;
    private readonly Guid first = Guid.NewGuid(), second = Guid.NewGuid();

    private ProjectInventory Project()
    {
        Directory.CreateDirectory(Path.Combine(root, "Data", "Project"));
        Directory.CreateDirectory(Path.Combine(root, "Data", "Results", "SortableTestsWithPrefix", "0"));
        return new ProjectInventory(root);
    }

    private string Result(Guid id) => Path.Combine(root, "Data", "Results", "SortableTestsWithPrefix", "0", id + ".msgpack");

    private void WriteGeneSortResult(Guid id)
    {
        var json = $$"""[[1,[{"Id":"{{id}}","ChannelCount":2,"SortableArrays":[{"Values":[false,true],"ChannelCount":2}]}]],[{"Index":1}]]""";
        File.WriteAllBytes(Result(id), MessagePackSerializer.ConvertFromJson(json));
    }

    [Fact]
    public async Task DesignerReadsGeneSortResultsAndSavesReorderedSelection()
    {
        var project = Project();
        WriteGeneSortResult(first);
        WriteGeneSortResult(second);
        var designer = new CsvInventoryViewModel(null);
        await designer.OpenAsync(project.ProjectDirectory);
        var rows = designer.Rows!;
        Assert.Equal(2, rows.Count);
        Assert.Equal("BoolArray", rows[0]["SortableDataFormat"]);
        Assert.Equal("1", rows[0]["PrefixCeCount"]);
        Assert.Equal("1", rows[0]["TestCount"]);
        Assert.Equal(Result(Guid.Parse((string)rows[0]["DataId"])), Path.GetFullPath((string)rows[0]["RelativePath"], project.ProjectDirectory));
        rows[0]["Order"] = "9";
        rows[1]["Order"] = "1";
        designer.ManifestName = "Reverse";
        designer.Publish.Execute(null);
        using var parser = Open(Path.Combine(project.ManifestsDirectory, "Reverse.csv"));
        var header = parser.ReadFields()!;
        int id = Array.IndexOf(header, "DataId");
        var a = parser.ReadFields()!; var b = parser.ReadFields()!;
        Assert.Equal("0", a[0]); Assert.Equal(rows[1]["DataId"], a[id]);
        Assert.Equal("1", b[0]); Assert.Equal(rows[0]["DataId"], b[id]);
        Assert.True(parser.EndOfData);

        var before = File.ReadAllBytes(project.InventoryPath);
        File.WriteAllBytes(Result(first), [0]);
        Assert.Throws<InvalidDataException>(() => project.Refresh());
        Assert.Equal(before, File.ReadAllBytes(project.InventoryPath));
    }

    [Fact]
    public void ManifestGroupsTypesPreservesCsvPropertiesAndRejectsDuplicateNames()
    {
        var project = Project();
        WriteGeneSortResult(first);
        string path = Path.GetRelativePath(project.ProjectDirectory, Result(first));
        File.WriteAllText(project.InventoryPath,
            $"OutputDataType,DataId,RelativePath,Comment\r\nZ,{first},{path},unused\r\nA,{second},{path},\"comma, quote \"\"and\"\"\r\nnewline\"\r\nZ,{second},{path},included\r\n");
        InventorySelection[] selection = [new("Z", second, 4), new("A", second, 4)];
        var file = project.SaveManifest("Two types", selection);
        using var parser = Open(file);
        Assert.Equal("Index", parser.ReadFields()![0]);
        Assert.Equal(new[] { "0", "A", second.ToString(), path, "comma, quote \"and\"\r\nnewline" }, parser.ReadFields());
        Assert.Equal(new[] { "0", "Z", second.ToString(), path, "included" }, parser.ReadFields());
        Assert.True(parser.EndOfData);
        Assert.Throws<IOException>(() => project.SaveManifest("Two types", selection));
        Assert.Throws<ArgumentException>(() => project.SaveManifest("Duplicates", [selection[0], selection[0]]));
    }

    private static TextFieldParser Open(string path)
    {
        var parser = new TextFieldParser(path) { TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        return parser;
    }

    public void Dispose() => Directory.Delete(root, true);
}
