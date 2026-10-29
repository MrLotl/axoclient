using System.IO.Compression;
using AxoClient.Core.Content;
using AxoClient.Core.Launch.Checks;
using System.Xml.Linq;
using AxoClient.Core.Game;

namespace AxoClient.Tests;

public class NeoForgeTests
{
    private static XDocument Maven(params string[] versions) =>
        new(new XElement("metadata", new XElement("versioning", new XElement("versions",
            versions.Select(v => new XElement("version", v))))));

    [Fact]
    public void LegacyVersions_NewestFirst_WithoutForgeDuplicates()
    {
        var neo = Maven("1.20.1-47.1.7", "1.20.1-47.1.5", "1.20.1-47.1.106", "1.20.1-47.1.99", "47.1.82");
        var forge = Maven("1.20.1-47.1.7", "1.20.1-47.4.0", "1.21.1-52.0.1");

        Assert.Equal(["47.1.106", "47.1.99", "47.1.5"], NeoForgeLoader.LegacyVersions(neo, forge));
    }

    [Theory]
    [InlineData("1.20.1", "47.1.106", "1.20.1-forge-47.1.106")]
    [InlineData("1.21.1", "21.1.200", "neoforge-21.1.200")]
    public void VersionId_MatchesInstallerProfile(string mc, string loader, string expected) =>
        Assert.Equal(expected, NeoForgeLoader.VersionId(mc, loader));

    [Fact]
    public void LegacyForgeVersion_PointsToNeoForgeMaven()
    {
        var version = NeoForgeLoader.LegacyForgeVersion("1.20.1", "47.1.106");

        Assert.Equal("https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar",
            version.GetInstallerFile()?.DirectUrl);
    }
}

public class LoaderDetectionTests
{
    private static ZipArchive Jar(params (string Name, string Text)[] files)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var (name, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private const string NeoToml = """
        modLoader = "javafml"
        [[mods]]
        modId = "preloading_tricks"
        """;

    [Fact]
    public void ForgeServiceJar_CountsAsForge()
    {
        // Wie Preloading Tricks: Fabric + NeoForge-Metadaten, Forge nur über einen Mod-Locator-Service
        using var zip = Jar(("fabric.mod.json", """{ "id": "preloading_tricks" }"""),
            ("META-INF/neoforge.mods.toml", NeoToml),
            ("META-INF/services/net.minecraftforge.forgespi.locating.IModLocator", "x.Locator"));

        var meta = ModMetadataReader.Read(zip)!.Meta;

        Assert.Contains("forge", meta.Loaders);
        Assert.True(CompatibilityCheck.LoaderFits(meta.Loaders, LoaderType.Forge, "1.20.1"));
    }

    [Fact]
    public void PreferForge_FallsBackToNeoForgeToml()
    {
        using var zip = Jar(("fabric.mod.json", """{ "id": "preloading_tricks" }"""), ("META-INF/neoforge.mods.toml", NeoToml));

        Assert.Equal("neoforge", ModMetadataReader.Read(zip, "forge")!.Meta.Loaders[0]);
    }

    [Theory]
    [InlineData("1.20.1", true)]
    [InlineData("1.20.4", true)]
    [InlineData("1.20.6", false)]
    [InlineData("1.21.1", false)]
    public void NeoForge_AcceptsForgeModsUntil1_20_4(string mc, bool fits) =>
        Assert.Equal(fits, CompatibilityCheck.LoaderFits(["forge"], LoaderType.NeoForge, mc));

    [Fact]
    public void NeoForgeOnlyMod_DoesNotFitForge() =>
        Assert.False(CompatibilityCheck.LoaderFits(["neoforge"], LoaderType.Forge, "1.20.1"));
}
