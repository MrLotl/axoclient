using System.IO.Abstractions;
using System.IO.Compression;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch;

namespace AxoClient.Tests;

/// <summary>Echte Ordner im Temp-Verzeichnis: ContentManager und Datapacks arbeiten direkt auf der Platte.</summary>
public abstract class TempInstanceTest : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "axo-test-" + Guid.NewGuid().ToString("N"));
    protected readonly AppPaths Paths;
    protected readonly InstanceManager Instances;
    protected readonly ContentManager Content;
    protected readonly DownloadManager Downloads;

    protected TempInstanceTest()
    {
        Paths = new AppPaths(Root);
        Instances = new InstanceManager(Paths, new EventHub(), new ProcessRunner());
        // Ohne Netz: die Tests laden nichts herunter
        Downloads = new DownloadManager(new ApiClient(new HttpClientHandler(), new FileSystem(), Paths.ApiCache), new FileSystem());
        Content = new ContentManager(Instances, new ContentProviders([]), Downloads, new EventHub());
    }

    protected static void WriteZip(string path, string entry = "x.txt")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
        writer.Write(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }
}

public class ContentRollbackTests : TempInstanceTest
{
    [Fact]
    public async Task RollbackRestoresPreviousFileAndEntry()
    {
        var instance = await Instances.CreateAsync(new CreateInstanceRequest("Test", "1.21.1", LoaderType.Fabric, "0.16.0"));
        var mods = Content.Folder(instance.Id, ContentType.Mod);
        WriteZip(Path.Combine(mods, "sodium-2.0.jar"));
        var previousDir = Path.Combine(Paths.Instance(instance.Id), "previous", "mods");
        WriteZip(Path.Combine(previousDir, "sodium-1.0.jar"));
        await Content.SyncAsync(instance.Id);
        await Content.ChangeAsync(instance.Id, doc =>
        {
            var entry = doc.Mods.Single(m => m.File == "sodium-2.0.jar");
            entry.Name = "Sodium";
            entry.Version = "2.0";
            entry.ProjectId = "AANobbMI";
            entry.Source = ContentSource.Modrinth;
            entry.Previous = new ContentEntry
            {
                File = "sodium-1.0.jar", Name = "Sodium", Version = "1.0", ProjectId = "AANobbMI", Source = ContentSource.Modrinth, Enabled = false
            };
        });

        var before = await Content.ListAsync(instance.Id, ContentType.Mod);
        Assert.Equal("1.0", before.Single().PreviousVersion);

        await Content.RollbackAsync(instance.Id, ContentType.Mod, "sodium-2.0.jar");

        var after = await Content.ListAsync(instance.Id, ContentType.Mod);
        var item = Assert.Single(after);
        Assert.Equal("sodium-1.0.jar", item.File);
        Assert.Equal("1.0", item.Version);
        Assert.Null(item.PreviousVersion);
        // War vor dem Update deaktiviert: bleibt es auch nach dem Zurücknehmen
        Assert.False(item.Enabled);
        Assert.True(File.Exists(Path.Combine(mods, "sodium-1.0.jar.disabled")));
        Assert.False(File.Exists(Path.Combine(mods, "sodium-2.0.jar")));
        Assert.False(File.Exists(Path.Combine(previousDir, "sodium-1.0.jar")));
    }

    [Fact]
    public async Task RollbackWithoutPreviousFails()
    {
        var instance = await Instances.CreateAsync(new CreateInstanceRequest("Test", "1.21.1", LoaderType.Fabric, "0.16.0"));
        WriteZip(Path.Combine(Content.Folder(instance.Id, ContentType.Mod), "lithium.jar"));
        await Content.SyncAsync(instance.Id);
        var ex = await Assert.ThrowsAsync<AxoException>(() => Content.RollbackAsync(instance.Id, ContentType.Mod, "lithium.jar"));
        Assert.Equal("content.noPrevious", ex.Message);
    }

    [Fact]
    public void CrashOffersRollbackOnlyForUpdatedMods()
    {
        var content = new ContentDocument
        {
            Mods =
            [
                new ContentEntry
                {
                    File = "sodium-2.0.jar", Name = "Sodium", Enabled = true,
                    Meta = new ModMetadata { ModId = "sodium", Packages = ["net.caffeinemc.sodium"] },
                    Previous = new ContentEntry { File = "sodium-1.0.jar", Version = "1.0" }
                },
                new ContentEntry
                {
                    File = "lithium.jar", Name = "Lithium", Enabled = true,
                    Meta = new ModMetadata { ModId = "lithium", Packages = ["net.caffeinemc.lithium"] }
                }
            ]
        };
        var log = "java.lang.NullPointerException\n\tat net.caffeinemc.sodium.render.Foo.bar(Foo.java:1)\n\tat net.caffeinemc.lithium.Baz.q(Baz.java:2)";
        var result = new CrashAnalyzer().Analyze(new CrashInput(1, null, null, log, null), content);
        Assert.Equal("rollbackMod", result.Actions[0]);
        Assert.True(result.Suspects.Single(s => s.Name == "Sodium").CanRollback);
        Assert.False(result.Suspects.Single(s => s.Name == "Lithium").CanRollback);
    }
}

public class DatapackTests : TempInstanceTest
{
    private DatapackManager Manager => new(Paths, Instances, new WorldManager(Paths), new ContentProviders([]), Downloads);

    private async Task<(string Id, string Dir)> WorldAsync()
    {
        var instance = await Instances.CreateAsync(new CreateInstanceRequest("Test", "1.21.1", LoaderType.Vanilla, null));
        var world = Path.Combine(Paths.GameDir(instance.Id), "saves", "Welt");
        Directory.CreateDirectory(world);
        await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "x");
        return (instance.Id, Path.Combine(world, "datapacks"));
    }

    [Fact]
    public async Task ListsZipsDisabledZipsAndPackFoldersOnly()
    {
        var (id, dir) = await WorldAsync();
        WriteZip(Path.Combine(dir, "terralith.zip"));
        WriteZip(Path.Combine(dir, "incendium.zip.disabled"));
        Directory.CreateDirectory(Path.Combine(dir, "eigenes"));
        await File.WriteAllTextAsync(Path.Combine(dir, "eigenes", "pack.mcmeta"), "{}");
        Directory.CreateDirectory(Path.Combine(dir, "kein-pack"));
        await File.WriteAllTextAsync(Path.Combine(dir, "notiz.txt"), "x");

        var items = await Manager.ListAsync(id, "Welt");

        Assert.Equal(["eigenes", "incendium.zip", "terralith.zip"], items.Select(i => i.File).ToArray());
        Assert.False(items.Single(i => i.File == "incendium.zip").Enabled);
        Assert.True(items.Single(i => i.File == "eigenes").IsFolder);
    }

    [Fact]
    public async Task TogglingRenamesTheZip()
    {
        var (id, dir) = await WorldAsync();
        WriteZip(Path.Combine(dir, "terralith.zip"));

        await Manager.SetEnabledAsync(id, "Welt", "terralith.zip", false);
        Assert.True(File.Exists(Path.Combine(dir, "terralith.zip.disabled")));
        Assert.False((await Manager.ListAsync(id, "Welt")).Single().Enabled);

        await Manager.SetEnabledAsync(id, "Welt", "terralith.zip", true);
        Assert.True(File.Exists(Path.Combine(dir, "terralith.zip")));
    }

    [Fact]
    public async Task FoldersCannotBeToggledAndPathsStayInsideTheWorld()
    {
        var (id, dir) = await WorldAsync();
        Directory.CreateDirectory(Path.Combine(dir, "eigenes"));
        var ex = await Assert.ThrowsAsync<AxoException>(() => Manager.SetEnabledAsync(id, "Welt", "eigenes", false));
        Assert.Equal("datapacks.folderToggle", ex.Message);
        await Assert.ThrowsAsync<AxoException>(() => Manager.SetEnabledAsync(id, "Welt", "..\\level.dat", false));
        await Assert.ThrowsAsync<AxoException>(() => Manager.ListAsync(id, "..\\..\\.."));
    }
}
