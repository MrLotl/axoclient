using System.Text.Json;
using AxoClient.Accounts.Contract;
using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Instances;
using AxoClient.DataModel.Social;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// Postfach: Instanzen (als Rezept), Mods, Server und Einladungen an Freunde senden und empfangen. Pakete enthalten
    /// nur IDs von Modrinth bzw. CurseForge; der Empfänger lädt selbst und prüft die Hashes. Nie Pfade, nie Java-Argumente.
    /// </summary>
    public sealed class InboxManager : IInboxManager
    {
        private readonly IOnlineClient _client;
        private readonly ISocialService _social;
        private readonly IInstanceManager _instances;
        private readonly IContentManager _content;
        private readonly IServerListManager _servers;
        private readonly IJobManager _jobs;
        private readonly ITextHelper _textHelper;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;
        private readonly ILoaderNames _loaderNames;
        private readonly IServerPing _serverPing;
        private readonly IOptionsFile _optionsFile;
        private readonly IGameVersionHelper _gameVersionHelper;
        private readonly IUuidHelper _uuidHelper;
        private const int MaxPayloadChars = 250_000;

        public InboxManager(
            IOnlineClient client,
            ISocialService social,
            IInstanceManager instances,
            IContentManager content,
            IServerListManager servers,
            IJobManager jobs,
            ITextHelper textHelper,
            IJsonHelper jsonHelper,
            IAppLog appLog,
            ILoaderNames loaderNames,
            IServerPing serverPing,
            IOptionsFile optionsFile,
            IGameVersionHelper gameVersionHelper,
            IUuidHelper uuidHelper)
        {
            _client = client;
            _social = social;
            _instances = instances;
            _content = content;
            _servers = servers;
            _jobs = jobs;
            _textHelper = textHelper;
            _jsonHelper = jsonHelper;
            _appLog = appLog;
            _loaderNames = loaderNames;
            _serverPing = serverPing;
            _optionsFile = optionsFile;
            _gameVersionHelper = gameVersionHelper;
            _uuidHelper = uuidHelper;
        }

        public async Task SendInstance(string instanceId, IReadOnlyCollection<string> recipients, bool includeServers, bool includeOptions,
            bool includeModLock, CancellationToken ct)
        {
            var instance = await _instances.Get(instanceId, ct);
            var doc = await _content.Sync(instanceId, ct);
            var recipe = new InstanceRecipe
            {
                Name = instance.Name,
                Minecraft = instance.Minecraft.Version,
                Loader = _loaderNames.ModrinthId(instance.Loader.Type),
                LoaderVersion = instance.Loader.Version,
                // Lokale Dateien ohne bekannte Quelle und die AxoClient-Mod werden nie mitgeschickt
                Content = Enum.GetValues<ContentType>().SelectMany(t => doc.ListOf(t).Select(e => (t, e)))
                    .Where(x => x.e.Enabled && x.e.Source is ContentSource.Modrinth or ContentSource.CurseForge && x.e.ProjectId != null
                                && x.e.VersionId != null)
                    .Select(x => new RecipeContent(x.t.ToString().ToLowerInvariant(),
                        x.e.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", x.e.ProjectId!, x.e.VersionId!,
                        includeModLock && x.t == ContentType.Mod && instance.ModLock.IsLocked(x.e)))
                    .ToList(),
                Servers = includeServers ? _servers.List(instanceId).Select(s => new RecipeServer(s.Name, s.Address)).ToList() : [],
                Options = includeOptions ? await readOptions(instanceId, ct) : null,
                ModLock = includeModLock && instance.ModLock.Active ? instance.ModLock.Level.ToString().ToLowerInvariant() : null
            };
            await send("instance", instance.Name, recipe, recipients, ct);
        }

        public Task SendMod(string title, RecipeContent mod, IReadOnlyCollection<string> recipients, CancellationToken ct) =>
            send("mod", title, mod, recipients, ct);

        public Task SendServer(string name, string address, IReadOnlyCollection<string> recipients, CancellationToken ct) =>
            _serverPing.IsValidAddress(address)
                ? send("server", name, new RecipeServer(_textHelper.Clean(name, 80), address.Trim()), recipients, ct)
                : throw new AxoException("servers.addressInvalid");

        public Task SendInvite(string address, string? instanceName, string? minecraft, IReadOnlyCollection<string> recipients,
            CancellationToken ct) =>
            send("invite", address, new InvitePayload(address, instanceName, minecraft), recipients, ct);

        private async Task send(string kind, string title, object payload, IReadOnlyCollection<string> recipients, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload, _jsonHelper.Compact);
            if (json.Length > MaxPayloadChars)
            {
                throw new AxoException("inbox.tooLarge");
            }

            if (recipients.Count is 0 or > 20)
            {
                throw new AxoException("inbox.noRecipients");
            }

            await _client.Send(HttpMethod.Post, "/inbox", new
            {
                recipients = recipients.Select(_uuidHelper.RequireUuid).ToArray(),
                kind,
                title = _textHelper.Clean(title, 80),
                payload = json
            }, ct);
        }

        public async Task<JsonElement> Fetch(string id, CancellationToken ct)
        {
            requireId(id);
            var item = await _client.Send<JsonElement>(HttpMethod.Get, $"/inbox/{id}", ct: ct);
            return item;
        }

        public async Task Discard(string id, CancellationToken ct)
        {
            await _client.Send(HttpMethod.Delete, $"/inbox/{requireId(id)}", ct: ct);
            await _social.SyncNow(ct);
        }

        /// <summary>Paket annehmen: Instanz anlegen, Mod installieren oder Server eintragen.</summary>
        public async Task<string?> Accept(string id, string? targetInstanceId, CancellationToken ct)
        {
            var item = await Fetch(id, ct);
            var kind = item.GetProperty("kind").GetString();
            var payloadText = item.GetProperty("payload").GetString() ?? "{}";
            if (payloadText.Length > MaxPayloadChars)
            {
                throw new AxoException("inbox.tooLarge");
            }

            string? result = null;
            switch (kind)
            {
                case "instance":
                    {
                        var recipe = JsonSerializer.Deserialize<InstanceRecipe>(payloadText, _jsonHelper.Options) ?? throw new AxoException("inbox.invalid");
                        result = await applyRecipe(recipe);
                        break;
                    }
                case "mod":
                    {
                        var mod = JsonSerializer.Deserialize<RecipeContent>(payloadText, _jsonHelper.Options) ?? throw new AxoException("inbox.invalid");
                        var target = targetInstanceId ?? throw new AxoException("inbox.chooseInstance");
                        var type = parseType(mod.Type);
                        await _jobs.Run("content.install", new TextRef("content.installing"),
                            job => _content.Install(target, type, new InstallRequest(requireSource(mod.Source), mod.ProjectId, mod.VersionId), job),
                            target, ct);
                        result = target;
                        break;
                    }
                case "server":
                    {
                        var server = JsonSerializer.Deserialize<RecipeServer>(payloadText, _jsonHelper.Options) ?? throw new AxoException("inbox.invalid");
                        var target = targetInstanceId ?? throw new AxoException("inbox.chooseInstance");
                        await _servers.Add(target, server.Name, server.Address, 0);
                        result = target;
                        break;
                    }
                case "invite":
                    // Einladungen werden in der UI über "Beitreten" angenommen
                    break;
                default:
                    throw new AxoException("inbox.invalid");
            }
            await Discard(id, ct);
            return result;
        }

        private Task<string> applyRecipe(InstanceRecipe recipe) =>
            _jobs.Run("inbox.instance", new TextRef("inbox.installingInstance", ("name", _textHelper.Clean(recipe.Name, 60))), async job =>
            {
                var loader = recipe.Loader switch
                {
                    "fabric" => LoaderType.Fabric,
                    "forge" => LoaderType.Forge,
                    "neoforge" => LoaderType.NeoForge,
                    "quilt" => LoaderType.Quilt,
                    _ => LoaderType.Vanilla
                };
                var instance = await _instances.Create(new CreateInstanceRequest(recipe.Name, recipe.Minecraft, loader,
                    _gameVersionHelper.IsSafeVersion(recipe.LoaderVersion) ? recipe.LoaderVersion : null), OriginType.Import, job.Token);
                var items = recipe.Content.Take(500).ToList();
                for (var i = 0; i < items.Count; i++)
                {
                    var c = items[i];
                    job.Report((double)i / items.Count);
                    try
                    {
                        await _content.Install(instance.Id, parseType(c.Type), new InstallRequest(requireSource(c.Source), c.ProjectId, c.VersionId),
                            job.Sub((double)i / items.Count, (double)(i + 1) / items.Count));
                    }
                    catch (AxoException ex)
                    {
                        _appLog.Warn($"Inhalt aus Paket übersprungen: {c.ProjectId}", ex);
                    }
                }
                foreach (var server in recipe.Servers.Take(50).Where(s => _serverPing.IsValidAddress(s.Address)))
                {
                    await _servers.Add(instance.Id, s(server.Name), server.Address);
                }

                if (recipe.Options != null)
                {
                    await writeOptions(instance.Id, recipe.Options, job.Token);
                }
                // Mod-Schutz des Absenders übernehmen: gesperrt sind genau die Mods, die bei ihm zum Pack gehörten
                if (recipe.ModLock is "pack" or "full")
                {
                    var created = await _instances.Get(instance.Id, job.Token);
                    created.ModLock = new ModLock
                    {
                        Level = recipe.ModLock == "full" ? ModLockLevel.Full : ModLockLevel.Pack,
                        PackMods = items.Where(c => c.Pack && c.Type == "mod").Select(c => "project:" + c.ProjectId).Distinct().ToList()
                    };
                    await _instances.Save(created, job.Token);
                }
                return instance.Id;

                string s(string name) => _textHelper.Clean(name, 80) is { Length: > 0 } n ? n : "Server";
            });

        private ContentType parseType(string type) => type switch
        {
            "mod" => ContentType.Mod,
            "resourcepack" => ContentType.ResourcePack,
            "shaderpack" => ContentType.ShaderPack,
            _ => throw new AxoException("inbox.invalid")
        };

        private string requireSource(string source) =>
            source is "modrinth" or "curseforge" ? source : throw new AxoException("inbox.invalid");

        private string requireId(string id) =>
            id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? id : throw new AxoException("errors.invalidId");

        private async Task<string?> readOptions(string instanceId, CancellationToken ct)
        {
            var path = Path.Combine(_instances.GameDir(instanceId), "options.txt");
            return File.Exists(path) ? _optionsFile.Filter(await File.ReadAllTextAsync(path, ct)) : null;
        }

        private async Task writeOptions(string instanceId, string options, CancellationToken ct) =>
            await File.WriteAllTextAsync(Path.Combine(_instances.GameDir(instanceId), "options.txt"), _optionsFile.Filter(options), ct);
    }
}
