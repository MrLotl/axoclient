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
            bool includeModLock, CancellationToken cancellationToken)
        {
            var instance = await _instances.Get(instanceId, cancellationToken);
            var document = await _content.Sync(instanceId, cancellationToken);
            var recipe = new InstanceRecipe
            {
                Name = instance.Name,
                Minecraft = instance.Minecraft.Version,
                Loader = _loaderNames.ModrinthId(instance.Loader.Type),
                LoaderVersion = instance.Loader.Version,
                // Lokale Dateien ohne bekannte Quelle und die AxoClient-Mod werden nie mitgeschickt
                Content = Enum.GetValues<ContentType>().SelectMany(contentType => document.ListOf(contentType).Select(entry => (Type: contentType, Entry: entry)))
                    .Where(contentItem => contentItem.Entry.Enabled && contentItem.Entry.Source is ContentSource.Modrinth or ContentSource.CurseForge && contentItem.Entry.ProjectId != null
                                && contentItem.Entry.VersionId != null)
                    .Select(contentItem => new RecipeContent(contentItem.Type.ToString().ToLowerInvariant(),
                        contentItem.Entry.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", contentItem.Entry.ProjectId!, contentItem.Entry.VersionId!,
                        includeModLock && contentItem.Type == ContentType.Mod && instance.ModLock.IsLocked(contentItem.Entry)))
                    .ToList(),
                Servers = includeServers ? _servers.List(instanceId).Select(server => new RecipeServer(server.Name, server.Address)).ToList() : [],
                Options = includeOptions ? await readOptions(instanceId, cancellationToken) : null,
                ModLock = includeModLock && instance.ModLock.Active ? instance.ModLock.Level.ToString().ToLowerInvariant() : null
            };
            await send("instance", instance.Name, recipe, recipients, cancellationToken);
        }

        public Task SendMod(string title, RecipeContent mod, IReadOnlyCollection<string> recipients, CancellationToken cancellationToken) =>
            send("mod", title, mod, recipients, cancellationToken);

        public Task SendServer(string name, string address, IReadOnlyCollection<string> recipients, CancellationToken cancellationToken) =>
            _serverPing.IsValidAddress(address)
                ? send("server", name, new RecipeServer(_textHelper.Clean(name, 80), address.Trim()), recipients, cancellationToken)
                : throw new AxoException("servers.addressInvalid");

        public Task SendInvite(string address, string? instanceName, string? minecraft, IReadOnlyCollection<string> recipients,
            CancellationToken cancellationToken) =>
            send("invite", address, new InvitePayload(address, instanceName, minecraft), recipients, cancellationToken);

        private async Task send(string kind, string title, object payload, IReadOnlyCollection<string> recipients, CancellationToken cancellationToken)
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
            }, cancellationToken);
        }

        public async Task<JsonElement> Fetch(string id, CancellationToken cancellationToken)
        {
            requireId(id);
            var item = await _client.Send<JsonElement>(HttpMethod.Get, $"/inbox/{id}", cancellationToken: cancellationToken);
            return item;
        }

        public async Task Discard(string id, CancellationToken cancellationToken)
        {
            await _client.Send(HttpMethod.Delete, $"/inbox/{requireId(id)}", cancellationToken: cancellationToken);
            await _social.SyncNow(cancellationToken);
        }

        /// <summary>Paket annehmen: Instanz anlegen, Mod installieren oder Server eintragen.</summary>
        public async Task<string?> Accept(string id, string? targetInstanceId, CancellationToken cancellationToken)
        {
            var item = await Fetch(id, cancellationToken);
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
                            target, cancellationToken);
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
            await Discard(id, cancellationToken);
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
                for (var index = 0; index < items.Count; index++)
                {
                    var recipeContent = items[index];
                    job.Report((double)index / items.Count);
                    try
                    {
                        await _content.Install(instance.Id, parseType(recipeContent.Type), new InstallRequest(requireSource(recipeContent.Source), recipeContent.ProjectId, recipeContent.VersionId),
                            job.Sub((double)index / items.Count, (double)(index + 1) / items.Count));
                    }
                    catch (AxoException exception)
                    {
                        _appLog.Warn($"Inhalt aus Paket übersprungen: {recipeContent.ProjectId}", exception);
                    }
                }
                foreach (var server in recipe.Servers.Take(50).Where(server => _serverPing.IsValidAddress(server.Address)))
                {
                    await _servers.Add(instance.Id, cleanServerName(server.Name), server.Address);
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
                        PackMods = items.Where(item => item.Pack && item.Type == "mod").Select(item => "project:" + item.ProjectId).Distinct().ToList()
                    };
                    await _instances.Save(created, job.Token);
                }
                return instance.Id;

                string cleanServerName(string name) => _textHelper.Clean(name, 80) is { Length: > 0 } cleanName ? cleanName : "Server";
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
            id.Length is > 0 and <= 64 && id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ? id : throw new AxoException("errors.invalidId");

        private async Task<string?> readOptions(string instanceId, CancellationToken cancellationToken)
        {
            var path = Path.Combine(_instances.GameDirectory(instanceId), "options.txt");
            return File.Exists(path) ? _optionsFile.Filter(await File.ReadAllTextAsync(path, cancellationToken)) : null;
        }

        private async Task writeOptions(string instanceId, string options, CancellationToken cancellationToken) =>
            await File.WriteAllTextAsync(Path.Combine(_instances.GameDirectory(instanceId), "options.txt"), _optionsFile.Filter(options), cancellationToken);
    }
}
