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
