using System.IO.Compression;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Launch;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Tests
{
    /// <summary>Echte Ordner im Temp-Verzeichnis: ContentManager und Datapacks arbeiten direkt auf der Platte.</summary>
    public abstract class TempInstanceTest : IDisposable
    {
        protected readonly string Root = Path.Combine(Path.GetTempPath(), "axo-test-" + Guid.NewGuid().ToString("N"));
        protected readonly TestServices Services;
        protected readonly AppPaths Paths;
        protected readonly IInstanceManager Instances;
        protected readonly IContentManager Content;
        protected readonly IDownloadManager Downloads;

        protected TempInstanceTest()
        {
            Services = new TestServices(Root);
            Paths = Services.Paths;
            Instances = Services.Get<IInstanceManager>();
            // Ohne Netz: die Tests laden nichts herunter
            Downloads = Services.Get<IDownloadManager>();
            Content = Services.Get<IContentManager>();
        }

        protected void WriteZip(string path, string entry = "x.txt")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }

    public class ContentRollbackTests : TempInstanceTest
    {
        [Fact]
        public async Task RollbackRestoresPreviousFileAndEntry()
        {
            var instance = await Instances.Create(new CreateInstanceRequest("Test", "1.21.1", LoaderType.Fabric, "0.16.0"));
            var mods = Content.Folder(instance.Id, ContentType.Mod);
            WriteZip(Path.Combine(mods, "sodium-2.0.jar"));
            var previousDirectory = Path.Combine(Paths.Instance(instance.Id), "previous", "mods");
            WriteZip(Path.Combine(previousDirectory, "sodium-1.0.jar"));
            await Content.Sync(instance.Id);
            await Content.Change(instance.Id, document =>
            {
                var entry = document.Mods.Single(mod => mod.File == "sodium-2.0.jar");
                entry.Name = "Sodium";
                entry.Version = "2.0";
                entry.ProjectId = "AANobbMI";
                entry.Source = ContentSource.Modrinth;
                entry.Previous = new ContentEntry
                {
                    File = "sodium-1.0.jar",
                    Name = "Sodium",
                    Version = "1.0",
                    ProjectId = "AANobbMI",
                    Source = ContentSource.Modrinth,
                    Enabled = false
                };
            });

            var before = await Content.List(instance.Id, ContentType.Mod);
            Assert.Equal("1.0", before.Single().PreviousVersion);

            await Content.Rollback(instance.Id, ContentType.Mod, "sodium-2.0.jar");

            var after = await Content.List(instance.Id, ContentType.Mod);
            var item = Assert.Single(after);
            Assert.Equal("sodium-1.0.jar", item.File);
            Assert.Equal("1.0", item.Version);
            Assert.Null(item.PreviousVersion);
            // War vor dem Update deaktiviert: bleibt es auch nach dem Zurücknehmen
            Assert.False(item.Enabled);
            Assert.True(File.Exists(Path.Combine(mods, "sodium-1.0.jar.disabled")));
            Assert.False(File.Exists(Path.Combine(mods, "sodium-2.0.jar")));
            Assert.False(File.Exists(Path.Combine(previousDirectory, "sodium-1.0.jar")));
        }

        [Fact]
        public async Task RollbackWithoutPreviousFails()
        {
            var instance = await Instances.Create(new CreateInstanceRequest("Test", "1.21.1", LoaderType.Fabric, "0.16.0"));
            WriteZip(Path.Combine(Content.Folder(instance.Id, ContentType.Mod), "lithium.jar"));
            await Content.Sync(instance.Id);
            var exception = await Assert.ThrowsAsync<AxoException>(() => Content.Rollback(instance.Id, ContentType.Mod, "lithium.jar"));
            Assert.Equal("content.noPrevious", exception.Message);
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
            var result = Services.Get<ICrashAnalyzer>().Analyze(new CrashInput(1, null, null, log, null), content);
            Assert.Equal("rollbackMod", result.Actions[0]);
            Assert.True(result.Suspects.Single(suspect => suspect.Name == "Sodium").CanRollback);
            Assert.False(result.Suspects.Single(suspect => suspect.Name == "Lithium").CanRollback);
        }
    }

    public class DatapackTests : TempInstanceTest
    {
        private IDatapackManager Manager => Services.Get<IDatapackManager>();

        private async Task<(string Id, string Dir)> world()
        {
            var instance = await Instances.Create(new CreateInstanceRequest("Test", "1.21.1", LoaderType.Vanilla, null));
            var world = Path.Combine(Paths.GameDirectory(instance.Id), "saves", "Welt");
            Directory.CreateDirectory(world);
            await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "x");
            return (instance.Id, Path.Combine(world, "datapacks"));
        }

        [Fact]
        public async Task ListsZipsDisabledZipsAndPackFoldersOnly()
        {
            var (id, directory) = await world();
            WriteZip(Path.Combine(directory, "terralith.zip"));
            WriteZip(Path.Combine(directory, "incendium.zip.disabled"));
            Directory.CreateDirectory(Path.Combine(directory, "eigenes"));
            await File.WriteAllTextAsync(Path.Combine(directory, "eigenes", "pack.mcmeta"), "{}");
            Directory.CreateDirectory(Path.Combine(directory, "kein-pack"));
            await File.WriteAllTextAsync(Path.Combine(directory, "notiz.txt"), "x");

            var items = await Manager.List(id, "Welt");

            Assert.Equal(["eigenes", "incendium.zip", "terralith.zip"], items.Select(item => item.File).ToArray());
            Assert.False(items.Single(item => item.File == "incendium.zip").Enabled);
            Assert.True(items.Single(item => item.File == "eigenes").IsFolder);
        }

        [Fact]
        public async Task TogglingRenamesTheZip()
        {
            var (id, directory) = await world();
            WriteZip(Path.Combine(directory, "terralith.zip"));

            await Manager.SetEnabled(id, "Welt", "terralith.zip", false);
            Assert.True(File.Exists(Path.Combine(directory, "terralith.zip.disabled")));
            Assert.False((await Manager.List(id, "Welt")).Single().Enabled);

            await Manager.SetEnabled(id, "Welt", "terralith.zip", true);
            Assert.True(File.Exists(Path.Combine(directory, "terralith.zip")));
        }

        [Fact]
        public async Task FoldersCannotBeToggledAndPathsStayInsideTheWorld()
        {
            var (id, directory) = await world();
            Directory.CreateDirectory(Path.Combine(directory, "eigenes"));
            var exception = await Assert.ThrowsAsync<AxoException>(() => Manager.SetEnabled(id, "Welt", "eigenes", false));
            Assert.Equal("datapacks.folderToggle", exception.Message);
            await Assert.ThrowsAsync<AxoException>(() => Manager.SetEnabled(id, "Welt", "..\\level.dat", false));
            await Assert.ThrowsAsync<AxoException>(() => Manager.List(id, "..\\..\\.."));
        }
    }
}
