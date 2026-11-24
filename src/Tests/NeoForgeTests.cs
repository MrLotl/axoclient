using System.IO.Compression;
using System.Xml.Linq;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Tests
{
    public class NeoForgeTests
    {
        private XDocument maven(params string[] versions) =>
            new(new XElement("metadata", new XElement("versioning", new XElement("versions",
                versions.Select(v => new XElement("version", v))))));

        [Fact]
        public void LegacyVersions_NewestFirst_WithoutForgeDuplicates()
        {
            var neo = maven("1.20.1-47.1.7", "1.20.1-47.1.5", "1.20.1-47.1.106", "1.20.1-47.1.99", "47.1.82");
            var forge = maven("1.20.1-47.1.7", "1.20.1-47.4.0", "1.21.1-52.0.1");

            Assert.Equal(["47.1.106", "47.1.99", "47.1.5"], new TestServices().Get<IEnumerable<ILoaderInstaller>>().OfType<NeoForgeLoader>().Single().LegacyVersions(neo, forge));
        }

        [Theory]
        [InlineData("1.20.1", "47.1.106", "1.20.1-forge-47.1.106")]
        [InlineData("1.21.1", "21.1.200", "neoforge-21.1.200")]
        public void VersionId_MatchesInstallerProfile(string mc, string loader, string expected) =>
            Assert.Equal(expected, new TestServices().Get<IGameVersionHelper>().NeoForgeVersionId(mc, loader));

        [Fact]
        public void LegacyForgeVersion_PointsToNeoForgeMaven()
        {
            var version = new TestServices().Get<IEnumerable<ILoaderInstaller>>().OfType<NeoForgeLoader>().Single().LegacyForgeVersion("1.20.1", "47.1.106");

            Assert.Equal("https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar",
                version.GetInstallerFile()?.DirectUrl);
        }
    }

    public class LoaderDetectionTests
    {
        private readonly IModMetadataReader _modMetadataReader;
        private ZipArchive jar(params (string Name, string Text)[] files)
        {
            var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                foreach (var (name, text) in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(text);
                }
            }

            buffer.Position = 0;
            return new ZipArchive(buffer, ZipArchiveMode.Read);
        }

        private const string NeoToml = """
        modLoader = "javafml"
        [[mods]]
        modId = "preloading_tricks"
        """;

        public LoaderDetectionTests()
        {
            var services = new TestServices();
            _modMetadataReader = services.Get<IModMetadataReader>();
        }

        [Fact]
        public void ForgeServiceJar_CountsAsForge()
        {
            // Wie Preloading Tricks: Fabric + NeoForge-Metadaten, Forge nur über einen Mod-Locator-Service
            using var zip = jar(("fabric.mod.json", """{ "id": "preloading_tricks" }"""),
                ("META-INF/neoforge.mods.toml", NeoToml),
                ("META-INF/services/net.minecraftforge.forgespi.locating.IModLocator", "x.Locator"));

            var meta = _modMetadataReader.Read(zip)!.Meta;

            Assert.Contains("forge", meta.Loaders);
            Assert.True(new TestServices().Get<IModCompatibility>().LoaderFits(meta.Loaders, LoaderType.Forge, "1.20.1"));
        }

        [Fact]
        public void PreferForge_FallsBackToNeoForgeToml()
        {
            using var zip = jar(("fabric.mod.json", """{ "id": "preloading_tricks" }"""), ("META-INF/neoforge.mods.toml", NeoToml));

            Assert.Equal("neoforge", _modMetadataReader.Read(zip, "forge")!.Meta.Loaders[0]);
        }

        [Theory]
        [InlineData("1.20.1", true)]
        [InlineData("1.20.4", true)]
        [InlineData("1.20.6", false)]
        [InlineData("1.21.1", false)]
        public void NeoForge_AcceptsForgeModsUntil1_20_4(string mc, bool fits) =>
            Assert.Equal(fits, new TestServices().Get<IModCompatibility>().LoaderFits(["forge"], LoaderType.NeoForge, mc));

        [Fact]
        public void NeoForgeOnlyMod_DoesNotFitForge() =>
            Assert.False(new TestServices().Get<IModCompatibility>().LoaderFits(["neoforge"], LoaderType.Forge, "1.20.1"));
    }

    public class ModAdaptTests
    {
        [Theory]
        [InlineData("forge", "[1.20.1,1.21)", LoaderType.Forge, "1.20.1", true)]
        [InlineData("neoforge", "[1.21.1,1.22)", LoaderType.Forge, "1.20.1", false)]
        [InlineData("forge", "[1.20.1,1.20.2)", LoaderType.Forge, "1.21.1", false)]
        [InlineData("fabric", null, LoaderType.Quilt, "1.21.1", true)]
        public void LocalMod_FitsByLoaderAndVersion(string modLoader, string? range, LoaderType loader, string mc, bool fits) =>
            Assert.Equal(fits, new TestServices().Get<IModCompatibility>().ModFits(new ModMetadata { Loaders = [modLoader], MinecraftRange = range }, loader, mc));

        [Fact]
        public void LocalModWithoutLoaderInfo_IsKept() =>
            Assert.True(new TestServices().Get<IModCompatibility>().ModFits(new ModMetadata(), LoaderType.Forge, "1.20.1"));
    }
}
