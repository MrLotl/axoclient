using System.IO.Compression;
using System.Text;
using AxoClient.Core.Content;
using AxoClient.Core.Instances;

namespace AxoClient.Tests;

public class MiniTomlTests
{
    [Fact]
    public void ParsesModsToml()
    {
        var tables = ModMetadataReader.MiniToml.Parse("""
            modLoader="javafml"
            [[mods]]
            modId = "examplemod" # Kommentar
            version = "1.2.3"
            description = '''
            Mehrzeilig
            '''
            [[dependencies.examplemod]]
            modId = "forge"
            versionRange = "[47,)"
            """);
        var mod = tables.First(t => t.Name == "mods");
        Assert.Equal("examplemod", mod.Get("modId"));
        Assert.Equal("1.2.3", mod.Get("version"));
        Assert.Equal("[47,)", tables.First(t => t.Name == "dependencies.examplemod").Get("versionRange"));
    }
}

public class ModMetadataReaderTests
{
    [Fact]
    public void ReadsFabricModJson()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("fabric.mod.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "schemaVersion": 1, "id": "sodium", "name": "Sodium", "version": "0.6.0", "authors": ["jellysquid"], "depends": { "minecraft": ">=1.21" } }""");
        }
        buffer.Position = 0;
        using var read = new ZipArchive(buffer, ZipArchiveMode.Read);
        var result = ModMetadataReader.Read(read);
        Assert.NotNull(result);
        Assert.Equal("sodium", result.Meta.ModId);
        Assert.Equal("Sodium", result.Name);
        Assert.Equal("0.6.0", result.Version);
    }
}

public class CurseForgeFingerprintTests
{
    [Fact]
    public void IgnoresWhitespace()
    {
        var a = CurseForgeProvider.Fingerprint(Encoding.ASCII.GetBytes("abc def\r\n"));
        var b = CurseForgeProvider.Fingerprint(Encoding.ASCII.GetBytes("abcdef"));
        Assert.Equal(a, b);
        Assert.NotEqual(a, CurseForgeProvider.Fingerprint(Encoding.ASCII.GetBytes("abcdeg")));
    }
}

public class NbtTests
{
    private static byte[] LevelDat(bool gzip)
    {
        var raw = new MemoryStream();
        var w = new BinaryWriter(raw);

        void Name(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            w.Write((byte)(b.Length >> 8));
            w.Write((byte)b.Length);
            w.Write(b);
        }

        w.Write((byte)10); Name("");
        w.Write((byte)10); Name("Data");
        w.Write((byte)8); Name("LevelName"); Name("Meine Welt");
        w.Write((byte)3); Name("GameType"); w.Write(new byte[] { 0, 0, 0, 1 });
        w.Write((byte)0);
        w.Write((byte)0);
        if (!gzip)
            return raw.ToArray();
        var packed = new MemoryStream();
        using (var g = new GZipStream(packed, CompressionMode.Compress, true))
            g.Write(raw.ToArray());
        return packed.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsLevelData(bool gzip)
    {
        var root = Nbt.Read(new MemoryStream(LevelDat(gzip)));
        var data = (NbtCompound)root["Data"];
        Assert.Equal("Meine Welt", data["LevelName"]);
        Assert.Equal(1, data["GameType"]);
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.ThrowsAny<Exception>(() => Nbt.Read(new MemoryStream([1, 2, 3])));
    }
}
