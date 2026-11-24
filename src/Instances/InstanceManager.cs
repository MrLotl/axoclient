using System.Collections.Concurrent;
using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>Anlegen, Ändern, Kopieren und Löschen von Instanzen. Jede Instanz ist ein eigener Ordner.</summary>
    public sealed class InstanceManager : IInstanceManager
    {
        private readonly AppPaths _paths;
        private readonly IEventHub _events;
        private readonly IProcessRunner _processes;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly IImageFormats _imageFormats;
        private readonly IInstanceRules _instanceRules;
        private readonly IGameVersionHelper _gameVersionHelper;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly ConcurrentDictionary<string, Instance> _cache = new();
        private readonly SemaphoreSlim _lock = new(1, 1);

        public InstanceManager(
            AppPaths paths,
            IEventHub events,
            IProcessRunner processes,
            ITextHelper textHelper,
            IAppLog appLog,
            IImageFormats imageFormats,
            IInstanceRules instanceRules,
            IGameVersionHelper gameVersionHelper,
            IJsonStoreFactory jsonStoreFactory)
        {
            _paths = paths;
            _events = events;
            _processes = processes;
            _textHelper = textHelper;
            _appLog = appLog;
            _imageFormats = imageFormats;
            _instanceRules = instanceRules;
            _gameVersionHelper = gameVersionHelper;
            _jsonStoreFactory = jsonStoreFactory;
        }

        public JsonStore<Instance> StoreOf(string id) => _jsonStoreFactory.Create<Instance>(_paths.Fs, _paths.InstanceJson(_textHelper.RequireId(id)));
        public JsonStore<ContentDocument> ContentOf(string id) => _jsonStoreFactory.Create<ContentDocument>(_paths.Fs, _paths.ContentJson(_textHelper.RequireId(id)));

        public string GameDirectory(string id) => _paths.GameDirectory(_textHelper.RequireId(id));

        public string ProcessKey(string id) => "instance:" + id;

        public IReadOnlyList<string> Ids() =>
            Directory.Exists(_paths.Instances)
                ? Directory.EnumerateDirectories(_paths.Instances).Select(Path.GetFileName).OfType<string>()
                    .Where(id => _textHelper.IsValidId(id) && File.Exists(_paths.InstanceJson(id))).ToList()
                : [];

        public async Task<IReadOnlyList<Instance>> LoadAll(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_paths.Instances))
            {
                return [];
            }

            var result = new List<Instance>();
            foreach (var directory in Directory.EnumerateDirectories(_paths.Instances))
            {
                var id = Path.GetFileName(directory);
                if (!_textHelper.IsValidId(id) || !File.Exists(_paths.InstanceJson(id)))
                {
                    continue;
                }

                try
                {
                    var instance = await StoreOf(id).Load(cancellationToken);
                    instance.Id = id;
                    _cache[id] = instance;
                    result.Add(instance);
                }
                catch (Exception exception) when (exception is IOException or AxoException)
                {
                    _appLog.Warn($"Instanz {id} konnte nicht gelesen werden", exception);
                }
            }
            return result;
        }

        public async Task<Instance> Get(string id, CancellationToken cancellationToken = default)
        {
            _textHelper.RequireId(id);
            if (_cache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (!File.Exists(_paths.InstanceJson(id)))
            {
                throw new AxoException("instances.notFound");
            }

            var instance = await StoreOf(id).Load(cancellationToken);
            instance.Id = id;
            _cache[id] = instance;
            return instance;
        }

        public async Task<IReadOnlyList<InstanceSummary>> List(CancellationToken cancellationToken = default)
        {
            var all = await LoadAll(cancellationToken);
            var summaries = new List<InstanceSummary>();
            foreach (var instance in all)
            {
                summaries.Add(await Summary(instance, cancellationToken));
            }

            return summaries.OrderByDescending(summary => summary.LastPlayed ?? summary.CreatedAt).ToList();
        }

        public async Task<InstanceSummary> Summary(Instance instance, CancellationToken cancellationToken = default)
        {
            var playtime = await _jsonStoreFactory.Create<ActivityLog>(_paths.Fs, _paths.PlaytimeJson(instance.Id)).Load(cancellationToken);
            var modCount = 0;
            if (File.Exists(_paths.ContentJson(instance.Id)))
            {
                modCount = (await ContentOf(instance.Id).Load(cancellationToken)).Mods.Count(mod => mod.Source != ContentSource.AxoClient);
            }

            var hasIcon = instance.Images.Icon != null && File.Exists(Path.Combine(_paths.Instance(instance.Id), instance.Images.Icon));
            var hasBanner = instance.Images.Banner != null && File.Exists(Path.Combine(_paths.Instance(instance.Id), instance.Images.Banner));
            var version = imageVersion(instance.Id);
            return new InstanceSummary(instance.Id, instance.Name, instance.Minecraft.Version, instance.Loader.Type,
                instance.Loader.Version, $"/axo/instance-icon/{instance.Id}?v={version}",
                hasBanner && !instance.Images.BannerAuto ? $"/axo/instance-banner/{instance.Id}?v={version}" : null,
                hasIcon, playtime.LastPlayed, playtime.TotalSeconds, _processes.Get(ProcessKey(instance.Id)) != null, modCount,
                instance.CreatedAt, instance.ModLock.Level, instance.Images.ArtSeed ?? instance.Id,
                instance.Images.GradientOnly, Math.Clamp(instance.Images.PanoramaView, 0, 3));
        }

        private bool isSafeSeed(string? seed) =>
            seed is { Length: > 0 and <= 64 } && seed.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

        private long imageVersion(string id)
        {
            var icon = Path.Combine(_paths.Instance(id), "icon.png");
            var banner = Path.Combine(_paths.Instance(id), "banner.png");
            return Math.Max(File.Exists(icon) ? File.GetLastWriteTimeUtc(icon).Ticks : 0,
                File.Exists(banner) ? File.GetLastWriteTimeUtc(banner).Ticks : 0) % 1_000_000_007;
        }

        public async Task<Instance> Create(CreateInstanceRequest request, OriginType origin = OriginType.Empty,
            CancellationToken cancellationToken = default)
        {
            var name = _instanceRules.CleanName(request.Name);
            if (!_gameVersionHelper.IsSafeVersion(request.MinecraftVersion))
            {
                throw new AxoException("instances.create.versionMissing");
            }

            if (request.LoaderVersion != null && !_gameVersionHelper.IsSafeVersion(request.LoaderVersion))
            {
                throw new AxoException("errors.invalidInput");
            }

            await _lock.WaitAsync(cancellationToken);
            try
            {
                var id = _textHelper.NewId(name);
                while (Directory.Exists(_paths.Instance(id)))
                {
                    id = _textHelper.NewId(name);
                }

                var instance = new Instance
                {
                    Id = id,
                    Name = name,
                    Minecraft = new MinecraftVersionRef(request.MinecraftVersion, VersionType.Release),
                    Loader = new LoaderRef(request.Loader, request.Loader == LoaderType.Vanilla ? null : request.LoaderVersion),
                    MemoryMb = request.MemoryMb is { } memoryMb ? Math.Clamp(memoryMb, 512, 65536) : null,
                    Origin = new InstanceOrigin { Type = origin },
                    CreatedAt = DateTimeOffset.UtcNow,
                    // Farbverlauf so, wie er beim Erstellen zu sehen war (nur harmlose Zeichen, sonst aus der ID)
                    Images = new InstanceImages { ArtSeed = isSafeSeed(request.ArtSeed) ? request.ArtSeed : null }
                };

                var directory = _paths.Instance(id);
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(_paths.GameDirectory(id));
                foreach (var sub in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "screenshots", "config" })
                {
                    Directory.CreateDirectory(Path.Combine(_paths.GameDirectory(id), sub));
                }

                Directory.CreateDirectory(_paths.InstanceBackups(id));

                await StoreOf(id).Save(instance, cancellationToken);
                await ContentOf(id).Save(new ContentDocument(), cancellationToken);
                _cache[id] = instance;
                publish();
                return instance;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Kopiert eine Instanz samt Welten und Mods. Spielzeit, Logs und Crash-Reports nicht.</summary>
        public async Task<Instance> Copy(string sourceId, string newName, JobContext job)
        {
            var source = await Get(sourceId, job.Token);
            var copy = await Create(new CreateInstanceRequest(newName, source.Minecraft.Version, source.Loader.Type,
                source.Loader.Version, source.MemoryMb), OriginType.Copy, job.Token);
            copy.JavaArgs = source.JavaArgs;
            copy.Minecraft = source.Minecraft;
            copy.AxoMod = source.AxoMod;
            copy.ModLock = new ModLock { Level = source.ModLock.Level, PackMods = [.. source.ModLock.PackMods] };
            copy.Origin.CopiedFrom = sourceId;

            var from = _paths.GameDirectory(sourceId);
            var to = _paths.GameDirectory(copy.Id);
            var files = Directory.Exists(from)
                ? Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
                    .Where(file => !IsTransient(Path.GetRelativePath(from, file))).ToList()
                : [];
            for (var index = 0; index < files.Count; index++)
            {
                job.Token.ThrowIfCancellationRequested();
                var target = Path.Combine(to, Path.GetRelativePath(from, files[index]));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(files[index], target, overwrite: true);
                job.Report((double)(index + 1) / files.Count);
            }
            foreach (var image in new[] { "icon.png", "banner.png" })
            {
                var file = Path.Combine(_paths.Instance(sourceId), image);
                if (File.Exists(file))
                {
                    File.Copy(file, Path.Combine(_paths.Instance(copy.Id), image), overwrite: true);
                }
            }
            copy.Images = new InstanceImages
            {
                Icon = source.Images.Icon,
                Banner = source.Images.Banner,
                BannerAuto = source.Images.BannerAuto,
                ArtSeed = source.Images.ArtSeed ?? source.Id,
                GradientOnly = source.Images.GradientOnly,
                PanoramaView = source.Images.PanoramaView
            };
            if (File.Exists(_paths.ContentJson(sourceId)))
            {
                File.Copy(_paths.ContentJson(sourceId), _paths.ContentJson(copy.Id), overwrite: true);
            }

            await Save(copy, job.Token);
            return copy;
        }

        public bool IsTransient(string relative)
        {
            var first = relative.Replace('\\', '/').Split('/')[0];
            return first is "logs" or "crash-reports" or ".fabric" or "debug" || relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                   || Path.GetFileName(relative).StartsWith("hs_err_pid");
        }

        public async Task<Instance> Update(string id, InstanceUpdate update, CancellationToken cancellationToken = default)
        {
            var instance = await Get(id, cancellationToken);
            if (update.Name != null)
            {
                instance.Name = _instanceRules.CleanName(update.Name);
            }

            if (update.MinecraftVersion != null)
            {
                if (!_gameVersionHelper.IsSafeVersion(update.MinecraftVersion))
                {
                    throw new AxoException("errors.invalidInput");
                }

                if (update.MinecraftVersion != instance.Minecraft.Version)
                {
                    instance.Minecraft = instance.Minecraft with { Version = update.MinecraftVersion };
                }
            }
            if (update.Loader is { } loader)
            {
                if (update.LoaderVersion != null && !_gameVersionHelper.IsSafeVersion(update.LoaderVersion))
                {
                    throw new AxoException("errors.invalidInput");
                }

                instance.Loader = new LoaderRef(loader, loader == LoaderType.Vanilla ? null : update.LoaderVersion);
            }
            if (update.ResetMemory)
            {
                instance.MemoryMb = null;
            }
            else if (update.MemoryMb is { } memoryMb)
            {
                instance.MemoryMb = Math.Clamp(memoryMb, 512, 65536);
            }

            if (update.JavaArgs != null)
            {
                instance.JavaArgs = _textHelper.Clean(update.JavaArgs, 2000);
            }

            if (update.BannerAuto is { } auto)
            {
                instance.Images.BannerAuto = auto;
            }

            if (update.AxoMod is { } axoMod)
            {
                instance.AxoMod = axoMod;
            }

            await Save(instance, cancellationToken);
            return instance;
        }

        public async Task Save(Instance instance, CancellationToken cancellationToken = default)
        {
            await StoreOf(instance.Id).Save(instance, cancellationToken);
            _cache[instance.Id] = instance;
            publish();
        }

        /// <summary>Löschen landet im Papierkorb, damit nichts unwiderruflich weg ist.</summary>
        public async Task Delete(string id, CancellationToken cancellationToken = default)
        {
            _textHelper.RequireId(id);
            if (_processes.Get(ProcessKey(id)) != null)
            {
                throw new AxoException("instances.running");
            }

            var directory = _paths.Instance(id);
            if (!Directory.Exists(directory))
            {
                return;
            }

            await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(directory,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), cancellationToken);
            _cache.TryRemove(id, out _);
            publish();
        }

        /// <summary>
        /// Hintergrund ohne eigenes Bild: Panorama der Version (Blickrichtung 0–3) oder nur Farbverlauf.
        /// Ein eigenes Hintergrundbild wird dabei entfernt.
        /// </summary>
        public async Task SetBackdrop(string id, bool gradientOnly, int panoramaView, CancellationToken cancellationToken = default)
        {
            var instance = await Get(id, cancellationToken);
            var banner = Path.Combine(_paths.Instance(id), "banner.png");
            if (File.Exists(banner))
            {
                File.Delete(banner);
            }

            instance.Images.Banner = null;
            instance.Images.BannerAuto = true;
            instance.Images.GradientOnly = gradientOnly;
            instance.Images.PanoramaView = Math.Clamp(panoramaView, 0, 3);
            await Save(instance, cancellationToken);
        }

        /// <summary>Speichert Instanzbild oder eigenes Hintergrundbild (intern „banner“). Nur PNG und JPEG, höchstens 5 MB.</summary>
        public async Task SetImage(string id, bool banner, byte[]? data, CancellationToken cancellationToken = default)
        {
            var instance = await Get(id, cancellationToken);
            var file = banner ? "banner.png" : "icon.png";
            var path = Path.Combine(_paths.Instance(id), file);
            if (data == null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (banner)
                {
                    instance.Images.Banner = null;
                    instance.Images.BannerAuto = true;
                }
                else
                {
                    instance.Images.Icon = null;
                }
            }
            else
            {
                if (data.Length > _imageFormats.MaxImageBytes || !_imageFormats.IsPngOrJpeg(data))
                {
                    throw new AxoException("errors.invalidImage");
                }

                await File.WriteAllBytesAsync(path, data, cancellationToken);
                if (banner)
                {
                    instance.Images.Banner = file;
                    instance.Images.BannerAuto = false;
                }
                else
                {
                    instance.Images.Icon = file;
                }
            }
            await Save(instance, cancellationToken);
        }

        public string? ImagePath(string id, bool banner)
        {
            if (!_textHelper.IsValidId(id))
            {
                return null;
            }

            var path = Path.Combine(_paths.Instance(id), banner ? "banner.png" : "icon.png");
            return File.Exists(path) ? path : null;
        }

        private void publish() => _events.Publish("instances.changed");
    }
}
