using System.IO.Compression;
using System.Text.Json;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Instances;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>
    /// Ganze Instanz als <c>.zip</c> sichern (Welten, Screenshots, instance.json, content.json, playtime.json) und wieder
    /// einlesen. Logs und Crash-Reports kommen nicht mit. Beim Import gelten die Sicherheitsregeln: Pfadprüfung,
    /// keine Java-Argumente übernehmen.
    /// </summary>
    public sealed class InstanceBackup : IInstanceBackup
    {
        private readonly AppPaths _paths;
        private readonly IInstanceManager _instances;
        private readonly IContentManager _content;
        private readonly IModpackManager _modpacks;
        private readonly IJsonHelper _jsonHelper;
        private readonly IPathGuard _pathGuard;
        private readonly IImageFormats _imageFormats;
        private readonly IInstanceRules _instanceRules;
        private readonly IGameVersionHelper _gameVersionHelper;
        private const string ManifestName = "axoclient-backup.json";

        public InstanceBackup(
            AppPaths paths,
            IInstanceManager instances,
            IContentManager content,
            IModpackManager modpacks,
            IJsonHelper jsonHelper,
            IPathGuard pathGuard,
            IImageFormats imageFormats,
            IInstanceRules instanceRules,
            IGameVersionHelper gameVersionHelper)
        {
            _paths = paths;
            _instances = instances;
            _content = content;
            _modpacks = modpacks;
            _jsonHelper = jsonHelper;
            _pathGuard = pathGuard;
            _imageFormats = imageFormats;
            _instanceRules = instanceRules;
            _gameVersionHelper = gameVersionHelper;
        }

        public async Task Export(string instanceId, string targetPath, JobContext job)
        {
            var instance = await _instances.Get(instanceId, job.Token);
            var root = _paths.Instance(instanceId);
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(r => !r.StartsWith("backups/") && !_instances.IsTransient(r.StartsWith("minecraft/") ? r["minecraft/".Length..] : r))
                .ToList();
            var tmp = targetPath + ".tmp";
            await using (var stream = File.Create(tmp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var manifest = zip.CreateEntry(ManifestName);
                await using (var writer = manifest.Open())
                {
                    await JsonSerializer.SerializeAsync(writer, new BackupManifest { InstanceName = instance.Name }, _jsonHelper.Options, job.Token);
                }

                for (var i = 0; i < files.Count; i++)
                {
                    job.Token.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(files[i], CompressionLevel.Fastest);
                    await using var source = new FileStream(Path.Combine(root, files[i]), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, job.Token);
                    job.Report((double)(i + 1) / files.Count);
                }
            }
            File.Move(tmp, targetPath, overwrite: true);
        }

        /// <summary>Erkennt AxoClient-Backup oder .mrpack und liest beides als neue Instanz ein.</summary>
        public async Task<Instance> Import(string archivePath, JobContext job)
        {
            if (archivePath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                return await _modpacks.ImportMrpack(archivePath, job);
            }

            using var zip = ZipFile.OpenRead(archivePath);
            if (zip.GetEntry("modrinth.index.json") != null)
            {
                return await _modpacks.ImportMrpack(archivePath, job);
            }

            var manifestEntry = zip.GetEntry(ManifestName) ?? throw new AxoException("import.unknownFormat");
            await using (var _ = manifestEntry.Open())
            {
            }
            var instanceEntry = zip.GetEntry("instance.json") ?? throw new AxoException("import.unknownFormat");
            Instance source;
            await using (var s = instanceEntry.Open())
            {
                source = await JsonSerializer.DeserializeAsync<Instance>(s, _jsonHelper.Options, job.Token) ?? throw new AxoException("import.unknownFormat");
            }

            var instance = await _instances.Create(new CreateInstanceRequest(_instanceRules.CleanName(source.Name), source.Minecraft.Version,
                source.Loader.Type, _gameVersionHelper.IsSafeVersion(source.Loader.Version) ? source.Loader.Version : null,
                source.MemoryMb), OriginType.Import, job.Token);
            var root = _paths.Instance(instance.Id);
            var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/') && e.FullName is not (ManifestName or "instance.json")).ToList();
            try
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    job.Token.ThrowIfCancellationRequested();
                    var target = _pathGuard.Combine(root, entries[i].FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var from = entries[i].Open();
                    await using var to = File.Create(target);
                    await from.CopyToAsync(to, job.Token);
                    job.Report((double)(i + 1) / entries.Count);
                }
            }
            catch
            {
                await _instances.Delete(instance.Id);
                throw;
            }

            // Java-Argumente aus fremden Archiven nie übernehmen; Bilder nur, wenn sie wirklich Bilder sind
            instance = await _instances.Get(instance.Id, job.Token);
            instance.JavaArgs = "";
            instance.Minecraft = source.Minecraft;
            instance.AxoMod = source.AxoMod;
            instance.ModLock = new ModLock { Level = source.ModLock.Level, PackMods = [.. source.ModLock.PackMods.Take(5000)] };
            instance.Images = new InstanceImages
            {
                Icon = File.Exists(Path.Combine(root, "icon.png")) && _imageFormats.IsPngOrJpeg(await File.ReadAllBytesAsync(Path.Combine(root, "icon.png"), job.Token)) ? "icon.png" : null,
                Banner = File.Exists(Path.Combine(root, "banner.png")) ? "banner.png" : null,
                BannerAuto = source.Images.BannerAuto
            };
            await _instances.Save(instance, job.Token);
            await _content.Sync(instance.Id, job.Token);
            return instance;
        }
    }
}
