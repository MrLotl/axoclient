using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using AxoClient.Core.Common;
using AxoClient.Core.Game;

namespace AxoClient.Tests;

public class PanoramaTests
{
    private const string Root = @"C:\axo";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[4000]];
    private static readonly byte[] Placeholder = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[70]];

    private static byte[] Jar(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        }
        return ms.ToArray();
    }

    private static (PanoramaService Service, MockFileSystem Fs, AppPaths Paths) Setup()
    {
        var fs = new MockFileSystem();
        var paths = new AppPaths(Root, fs);
        return (new PanoramaService(paths), fs, paths);
    }

    [Fact]
    public void ReadsPanoramaFromVersionJar()
    {
        var (service, fs, paths) = Setup();
        var jar = fs.Path.Combine(paths.Versions, "1.21.4", "1.21.4.jar");
        fs.AddFile(jar, new MockFileData(Jar(("assets/minecraft/textures/gui/title/background/panorama_0.png", Png))));

        Assert.Equal(Png, service.Get("1.21.4"));
    }

    [Fact]
    public void ReadsPanoramaFromAssetIndex()
    {
        var (service, fs, paths) = Setup();
        const string hash = "f5e0a5cfbc2e7b1e89094d7882dde106b030ec26";
        fs.AddFile(fs.Path.Combine(paths.Versions, "1.21.1", "1.21.1.json"), new MockFileData("""{ "id": "1.21.1", "assetIndex": { "id": "17" } }"""));
        fs.AddFile(fs.Path.Combine(paths.Assets, "indexes", "17.json"), new MockFileData(
            """{ "objects": { "minecraft/textures/gui/title/background/panorama_0.png": { "hash": "f5e0a5cfbc2e7b1e89094d7882dde106b030ec26", "size": 4008 } } }"""));
        fs.AddFile(fs.Path.Combine(paths.Assets, "objects", hash[..2], hash), new MockFileData(Png));
        // Die Jar enthält nur den winzigen Platzhalter, der darf nicht gewinnen
        fs.AddFile(fs.Path.Combine(paths.Versions, "1.21.1", "1.21.1.jar"), new MockFileData(Jar(("assets/minecraft/textures/gui/title/background/panorama_0.png", Placeholder))));

        Assert.Equal(Png, service.Get("1.21.1"));
    }

    [Fact]
    public void PlaceholderInJarGivesNull()
    {
        var (service, fs, paths) = Setup();
        fs.AddFile(fs.Path.Combine(paths.Versions, "26.2", "26.2.jar"), new MockFileData(Jar(("assets/minecraft/textures/gui/title/background/panorama_0.png", Placeholder))));
        Assert.Null(service.Get("26.2"));
    }

    [Fact]
    public void MissingJarOrEntryGivesNull()
    {
        var (service, fs, paths) = Setup();
        Assert.Null(service.Get("1.21.4"));

        var jar = fs.Path.Combine(paths.Versions, "b1.7.3", "b1.7.3.jar");
        fs.AddFile(jar, new MockFileData(Jar(("terrain.png", Png))));
        Assert.Null(service.Get("b1.7.3"));
    }

    [Fact]
    public void NotAPngGivesNull()
    {
        var (service, fs, paths) = Setup();
        var jar = fs.Path.Combine(paths.Versions, "1.20.1", "1.20.1.jar");
        fs.AddFile(jar, new MockFileData(Jar(("assets/minecraft/textures/gui/title/background/panorama_0.png", "kein Bild"u8.ToArray()))));
        Assert.Null(service.Get("1.20.1"));
    }

    [Fact]
    public void FoundAfterJarWasDownloaded()
    {
        var (service, fs, paths) = Setup();
        Assert.Null(service.Get("1.21.4"));
        fs.AddFile(fs.Path.Combine(paths.Versions, "1.21.4", "1.21.4.jar"), new MockFileData(Jar(("assets/minecraft/textures/gui/title/background/panorama_0.png", Png))));
        Assert.NotNull(service.Get("1.21.4"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../launcher")]
    [InlineData("1.21.4/../../x")]
    [InlineData("C:\\Windows")]
    [InlineData("")]
    public void RejectsUnsafeVersionNames(string version)
    {
        var (service, _, _) = Setup();
        Assert.False(PanoramaService.IsValidVersion(version));
        Assert.Null(service.Get(version));
    }

    [Theory]
    [InlineData("1.21.4")]
    [InlineData("24w14a")]
    [InlineData("1.20.1-pre2")]
    public void AcceptsNormalVersionNames(string version) => Assert.True(PanoramaService.IsValidVersion(version));

    [Fact]
    public void RejectsInvalidFace()
    {
        var (service, _, _) = Setup();
        Assert.Null(service.Get("1.21.4", 6));
    }
}
