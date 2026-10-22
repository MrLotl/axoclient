using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Game;
using AxoClient.Core.Instances;

namespace AxoClient.Core.Social;

/// <summary>
/// Postfach: Instanzen (als Rezept), Mods, Server und Einladungen an Freunde senden und empfangen. Pakete enthalten
/// nur IDs von Modrinth bzw. CurseForge; der Empfänger lädt selbst und prüft die Hashes. Nie Pfade, nie Java-Argumente.
/// </summary>
public sealed class InboxManager(
    OnlineClient client,
    SocialService social,
    InstanceManager instances,
    ContentManager content,
    ServerListManager servers,
    JobManager jobs)
{
    private const int MaxPayloadChars = 250_000;

    public async Task SendInstanceAsync(string instanceId, IReadOnlyCollection<string> recipients, bool includeServers, bool includeOptions,
        bool includeModLock, CancellationToken ct)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        var doc = await content.SyncAsync(instanceId, ct);
        var recipe = new InstanceRecipe
        {
            Name = instance.Name,
            Minecraft = instance.Minecraft.Version,
            Loader = LoaderNames.ModrinthId(instance.Loader.Type),
            LoaderVersion = instance.Loader.Version,
            // Lokale Dateien ohne bekannte Quelle und die AxoClient-Mod werden nie mitgeschickt
            Content = Enum.GetValues<ContentType>().SelectMany(t => doc.ListOf(t).Select(e => (t, e)))
                .Where(x => x.e.Enabled && x.e.Source is ContentSource.Modrinth or ContentSource.CurseForge && x.e.ProjectId != null
                            && x.e.VersionId != null)
                .Select(x => new RecipeContent(x.t.ToString().ToLowerInvariant(),
                    x.e.Source == ContentSource.CurseForge ? "curseforge" : "modrinth", x.e.ProjectId!, x.e.VersionId!,
                    includeModLock && x.t == ContentType.Mod && instance.ModLock.IsLocked(x.e)))
                .ToList(),
            Servers = includeServers ? servers.List(instanceId).Select(s => new RecipeServer(s.Name, s.Address)).ToList() : [],
            Options = includeOptions ? await ReadOptionsAsync(instanceId, ct) : null,
            ModLock = includeModLock && instance.ModLock.Active ? instance.ModLock.Level.ToString().ToLowerInvariant() : null
        };
        await SendAsync("instance", instance.Name, recipe, recipients, ct);
    }

    public Task SendModAsync(string title, RecipeContent mod, IReadOnlyCollection<string> recipients, CancellationToken ct) =>
        SendAsync("mod", title, mod, recipients, ct);

    public Task SendServerAsync(string name, string address, IReadOnlyCollection<string> recipients, CancellationToken ct) =>
        ServerPing.IsValidAddress(address)
            ? SendAsync("server", name, new RecipeServer(Text.Clean(name, 80), address.Trim()), recipients, ct)
            : throw new AxoException("servers.addressInvalid");

    public Task SendInviteAsync(string address, string? instanceName, string? minecraft, IReadOnlyCollection<string> recipients,
        CancellationToken ct) =>
        SendAsync("invite", address, new InvitePayload(address, instanceName, minecraft), recipients, ct);

    private async Task SendAsync(string kind, string title, object payload, IReadOnlyCollection<string> recipients, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, Json.Compact);
        if (json.Length > MaxPayloadChars)
            throw new AxoException("inbox.tooLarge");
        if (recipients.Count is 0 or > 20)
            throw new AxoException("inbox.noRecipients");
        await client.SendAsync(HttpMethod.Post, "/inbox", new
        {
            recipients = recipients.Select(SocialService.RequireUuid).ToArray(),
            kind,
            title = Text.Clean(title, 80),
            payload = json
        }, ct);
    }

    public async Task<JsonElement> FetchAsync(string id, CancellationToken ct)
    {
        RequireId(id);
        var item = await client.SendAsync<JsonElement>(HttpMethod.Get, $"/inbox/{id}", ct: ct);
        return item;
    }

    public async Task DiscardAsync(string id, CancellationToken ct)
    {
        await client.SendAsync(HttpMethod.Delete, $"/inbox/{RequireId(id)}", ct: ct);
        await social.SyncNowAsync(ct);
    }

    /// <summary>Paket annehmen: Instanz anlegen, Mod installieren oder Server eintragen.</summary>
    public async Task<string?> AcceptAsync(string id, string? targetInstanceId, CancellationToken ct)
    {
        var item = await FetchAsync(id, ct);
        var kind = item.GetProperty("kind").GetString();
        var payloadText = item.GetProperty("payload").GetString() ?? "{}";
        if (payloadText.Length > MaxPayloadChars)
            throw new AxoException("inbox.tooLarge");
        string? result = null;
        switch (kind)
        {
            case "instance":
            {
                var recipe = JsonSerializer.Deserialize<InstanceRecipe>(payloadText, Json.Options) ?? throw new AxoException("inbox.invalid");
                result = await ApplyRecipeAsync(recipe);
                break;
            }
            case "mod":
            {
                var mod = JsonSerializer.Deserialize<RecipeContent>(payloadText, Json.Options) ?? throw new AxoException("inbox.invalid");
                var target = targetInstanceId ?? throw new AxoException("inbox.chooseInstance");
                var type = ParseType(mod.Type);
                await jobs.RunAsync("content.install", new TextRef("content.installing"),
                    job => content.InstallAsync(target, type, new InstallRequest(RequireSource(mod.Source), mod.ProjectId, mod.VersionId), job),
                    target, ct);
                result = target;
                break;
            }
            case "server":
            {
                var server = JsonSerializer.Deserialize<RecipeServer>(payloadText, Json.Options) ?? throw new AxoException("inbox.invalid");
                var target = targetInstanceId ?? throw new AxoException("inbox.chooseInstance");
                await servers.AddAsync(target, server.Name, server.Address, 0);
                result = target;
                break;
            }
            case "invite":
                // Einladungen werden in der UI über "Beitreten" angenommen
                break;
            default:
                throw new AxoException("inbox.invalid");
        }
        await DiscardAsync(id, ct);
        return result;
    }

    private Task<string> ApplyRecipeAsync(InstanceRecipe recipe) =>
        jobs.RunAsync("inbox.instance", TextRef.Of("inbox.installingInstance", ("name", Text.Clean(recipe.Name, 60))), async job =>
        {
            var loader = recipe.Loader switch
            {
                "fabric" => LoaderType.Fabric,
                "forge" => LoaderType.Forge,
                "neoforge" => LoaderType.NeoForge,
                "quilt" => LoaderType.Quilt,
                _ => LoaderType.Vanilla
            };
            var instance = await instances.CreateAsync(new CreateInstanceRequest(recipe.Name, recipe.Minecraft, loader,
                LoaderManager.IsSafeVersion(recipe.LoaderVersion) ? recipe.LoaderVersion : null), OriginType.Import, job.Token);
            var items = recipe.Content.Take(500).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var c = items[i];
                job.Report((double)i / items.Count);
                try
                {
                    await content.InstallAsync(instance.Id, ParseType(c.Type), new InstallRequest(RequireSource(c.Source), c.ProjectId, c.VersionId),
                        job.Sub((double)i / items.Count, (double)(i + 1) / items.Count));
                }
                catch (AxoException ex)
                {
                    AppLog.Warn($"Inhalt aus Paket übersprungen: {c.ProjectId}", ex);
                }
            }
            foreach (var server in recipe.Servers.Take(50).Where(s => ServerPing.IsValidAddress(s.Address)))
                await servers.AddAsync(instance.Id, s(server.Name), server.Address);
            if (recipe.Options != null)
                await WriteOptionsAsync(instance.Id, recipe.Options, job.Token);
            // Mod-Schutz des Absenders übernehmen: gesperrt sind genau die Mods, die bei ihm zum Pack gehörten
            if (recipe.ModLock is "pack" or "full")
            {
                var created = await instances.GetAsync(instance.Id, job.Token);
                created.ModLock = new ModLock
                {
                    Level = recipe.ModLock == "full" ? ModLockLevel.Full : ModLockLevel.Pack,
                    PackMods = items.Where(c => c.Pack && c.Type == "mod").Select(c => "project:" + c.ProjectId).Distinct().ToList()
                };
                await instances.SaveAsync(created, job.Token);
            }
            return instance.Id;

            static string s(string name) => Text.Clean(name, 80) is { Length: > 0 } n ? n : "Server";
        });

    private static ContentType ParseType(string type) => type switch
    {
        "mod" => ContentType.Mod,
        "resourcepack" => ContentType.ResourcePack,
        "shaderpack" => ContentType.ShaderPack,
        _ => throw new AxoException("inbox.invalid")
    };

    private static string RequireSource(string source) =>
        source is "modrinth" or "curseforge" ? source : throw new AxoException("inbox.invalid");

    private static string RequireId(string id) =>
        id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? id : throw new AxoException("errors.invalidId");

    private async Task<string?> ReadOptionsAsync(string instanceId, CancellationToken ct)
    {
        var path = Path.Combine(instances.GameDir(instanceId), "options.txt");
        return File.Exists(path) ? OptionsFile.Filter(await File.ReadAllTextAsync(path, ct)) : null;
    }

    private async Task WriteOptionsAsync(string instanceId, string options, CancellationToken ct) =>
        await File.WriteAllTextAsync(Path.Combine(instances.GameDir(instanceId), "options.txt"), OptionsFile.Filter(options), ct);
}

/// <summary>
/// options.txt von Freunden: nur bekannte, harmlose Schlüssel (Grafik, Steuerung, Sound). Unbekannte Schlüssel werden
/// verworfen, z. B. Ressourcepack-Listen oder Pfade.
/// </summary>
public static class OptionsFile
{
    private static readonly string[] AllowedPrefixes =
    [
        "key_", "soundCategory_", "modelPart_", "mouseSensitivity", "fov", "gamma", "renderDistance", "simulationDistance",
        "guiScale", "particles", "maxFps", "graphicsMode", "ao", "biomeBlendRadius", "entityDistanceScaling", "renderClouds",
        "chatOpacity", "chatScale", "chatWidth", "chatHeightFocused", "chatHeightUnfocused", "autoJump", "toggleCrouch", "toggleSprint",
        "invertYMouse", "bobView", "enableVsync", "fullscreen", "showSubtitles", "narrator", "lang", "darkMojangStudiosBackground",
        "entityShadows", "fovEffectScale", "screenEffectScale", "damageTiltStrength", "mainHand", "attackIndicator", "chatColors",
        "chatLinks", "reducedDebugInfo", "mipmapLevels", "cloudRange", "menuBackgroundBlurriness"
    ];

    public static string Filter(string text) =>
        string.Join('\n', text.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.IndexOf(':') is > 0 and var colon && AllowedPrefixes.Any(p => l[..colon].StartsWith(p, StringComparison.Ordinal))
                        && l.Length < 200 && !l.Contains('\\') && !l.Contains('/'))
            .Take(400));
}
