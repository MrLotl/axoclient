using System.IO.Compression;
using System.Text;
using AxoClient.Content;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Instances;
using AxoClient.Instances.Contract;

namespace AxoClient.Tests
{
    public class MiniTomlTests
    {
        [Fact]
        public void ParsesModsToml()
        {
            var tables = new ModMetadataReader.MiniToml().Parse("""
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
            var mod = tables.First(table => table.Name == "mods");
            Assert.Equal("examplemod", mod.Get("modId"));
            Assert.Equal("1.2.3", mod.Get("version"));
            Assert.Equal("[47,)", tables.First(table => table.Name == "dependencies.examplemod").Get("versionRange"));
        }
    }

    public class ModMetadataReaderTests
    {
        private readonly IModMetadataReader _modMetadataReader;
        public ModMetadataReaderTests()
        {
            var services = new TestServices();
            _modMetadataReader = services.Get<IModMetadataReader>();
        }

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
            var result = _modMetadataReader.Read(read);
            Assert.NotNull(result);
            Assert.Equal("sodium", result.Meta.ModId);
            Assert.Equal("Sodium", result.Name);
            Assert.Equal("0.6.0", result.Version);
        }
    }

    public class CurseForgeFingerprintTests
    {
        private readonly CurseForgeProvider _curseForge = new TestServices().Create<CurseForgeProvider>("https://localhost", false);

        [Fact]
        public void IgnoresWhitespace()
        {
            var withWhitespace = _curseForge.Fingerprint(Encoding.ASCII.GetBytes("abc def\r\n"));
            var withoutWhitespace = _curseForge.Fingerprint(Encoding.ASCII.GetBytes("abcdef"));
            Assert.Equal(withWhitespace, withoutWhitespace);
            Assert.NotEqual(withWhitespace, _curseForge.Fingerprint(Encoding.ASCII.GetBytes("abcdeg")));
        }
    }

    public class NbtTests
    {
        private readonly INbtReader _nbtReader;
        public NbtTests()
        {
            var services = new TestServices();
            _nbtReader = services.Get<INbtReader>();
        }

        private byte[] levelDat(bool gzip)
        {
            var raw = new MemoryStream();
            var binaryWriter = new BinaryWriter(raw);

            void Name(string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                binaryWriter.Write((byte)(bytes.Length >> 8));
                binaryWriter.Write((byte)bytes.Length);
                binaryWriter.Write(bytes);
            }

            binaryWriter.Write((byte)10); Name("");
            binaryWriter.Write((byte)10); Name("Data");
            binaryWriter.Write((byte)8); Name("LevelName"); Name("Meine Welt");
            binaryWriter.Write((byte)3); Name("GameType"); binaryWriter.Write(new byte[] { 0, 0, 0, 1 });
            binaryWriter.Write((byte)0);
            binaryWriter.Write((byte)0);
            if (!gzip)
            {
                return raw.ToArray();
            }

            var packed = new MemoryStream();
            using (var gzipStream = new GZipStream(packed, CompressionMode.Compress, true))
            {
                gzipStream.Write(raw.ToArray());
            }

            return packed.ToArray();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReadsLevelData(bool gzip)
        {
            var root = _nbtReader.Read(new MemoryStream(levelDat(gzip)));
            var data = (NbtCompound)root["Data"];
            Assert.Equal("Meine Welt", data["LevelName"]);
            Assert.Equal(1, data["GameType"]);
        }

        [Fact]
        public void RejectsGarbage()
        {
            Assert.ThrowsAny<Exception>(() => _nbtReader.Read(new MemoryStream([1, 2, 3])));
        }
    }
}
