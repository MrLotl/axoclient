using System.Text.Json;

namespace AxoClient.Sharing;

public abstract class SharePayload
{
    public const int CurrentVersion = 1;

    public string Format { get; set; } = "";
    public int Version { get; set; } = CurrentVersion;

    public abstract void Validate();

    protected static string Clean(string? text, int maxLength, string fallback = "") =>
        Sanitize.Text(text, maxLength) is { Length: > 0 } clean ? clean : fallback;
}

public class ManifestContent
{
    public ContentType Type { get; set; }
    public string ProjectId { get; set; } = "";
    public string? VersionId { get; set; }
    public string Title { get; set; } = "";
    public string? VersionName { get; set; }
    public bool Enabled { get; set; } = true;
}

public class ManifestServer
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}

public sealed class InstanceManifest : SharePayload
{
    public InstanceManifest() => Format = ShareKinds.FormatOf(ShareKinds.Instance);

    public string Name { get; set; } = "";
    public string Minecraft { get; set; } = "";
    public LoaderType Loader { get; set; }
    public string? LoaderVersion { get; set; }
    public List<ManifestContent> Content { get; set; } = [];
    public List<ManifestServer> Servers { get; set; } = [];
    public string? Options { get; set; }
    public JsonElement? Overlay { get; set; }

    public override void Validate()
    {
        Name = Clean(Name, 60);
        if (!Enum.IsDefined(Loader))
            throw new ShareFormatException("Unbekannter Mod-Loader in der Instanz.");
        if (!ShareValidation.IsVersion(Minecraft))
            throw new ShareFormatException("Die Minecraft-Version in der Instanz ist ungültig.");
        if (LoaderVersion != null && !GameInstaller.IsSafeLoaderVersion(LoaderVersion))
            LoaderVersion = null;

        Content = (Content ?? []).Where(c => c != null).ToList();
        Servers = (Servers ?? []).Where(s => s != null).ToList();
        if (Content.Count > ShareValidation.MaxContentEntries)
            throw new ShareFormatException($"Die Instanz enthält zu viele Inhalte (mehr als {ShareValidation.MaxContentEntries}).");
        if (Servers.Count > ShareValidation.MaxServers)
            throw new ShareFormatException($"Die Instanz enthält zu viele Server (mehr als {ShareValidation.MaxServers}).");

        var seen = new HashSet<(ContentType, string)>();
        var cleaned = new List<ManifestContent>();
        foreach (var entry in Content)
        {
            if (!Enum.IsDefined(entry.Type) || !ShareValidation.IsModrinthId(entry.ProjectId))
                throw new ShareFormatException("Ein Eintrag in der Instanz hat keine gültige Modrinth-Kennung.");
            if (!seen.Add((entry.Type, entry.ProjectId)))
                continue;
            if (entry.VersionId != null && !ShareValidation.IsModrinthId(entry.VersionId))
                entry.VersionId = null;
            entry.Title = Clean(entry.Title, 100, entry.ProjectId);
            entry.VersionName = entry.VersionName == null ? null : Clean(entry.VersionName, 60);
            cleaned.Add(entry);
        }
        Content = cleaned;

        Servers = Servers
            .Where(s => ShareValidation.IsServerAddress(s.Address?.Trim()))
            .Select(s => new ManifestServer { Name = Clean(s.Name, 64, s.Address!.Trim()), Address = s.Address!.Trim() })
            .ToList();

        if (Options != null && (Options.Length > ShareValidation.MaxOptionsChars || Options.Contains('\0')))
            Options = null;
        if (Overlay is { } overlay
            && (overlay.ValueKind != JsonValueKind.Object || overlay.GetRawText().Length > ShareValidation.MaxOverlayChars))
            Overlay = null;
    }

    public string Describe()
    {
        var parts = new List<string>();

        void Add(int count, string singular, string plural)
        {
            if (count > 0)
                parts.Add(Formats.Count(count, singular, plural));
        }

        Add(Content.Count(c => c.Type == ContentType.Mod), "Mod", "Mods");
        Add(Content.Count(c => c.Type == ContentType.ResourcePack), "Ressourcenpaket", "Ressourcenpakete");
        Add(Content.Count(c => c.Type == ContentType.Shader), "Shader", "Shader");
        Add(Servers.Count, "Server", "Server");
        if (Options != null)
            parts.Add("Einstellungen");
        if (Overlay != null)
            parts.Add("Overlay");
        return parts.Count == 0 ? "Enthält nur Version und Mod-Loader." : "Enthält: " + string.Join(", ", parts) + ".";
    }
}

public sealed class OverlayPayload : SharePayload
{
    public OverlayPayload() => Format = ShareKinds.FormatOf(ShareKinds.Overlay);

    public string Name { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public JsonElement Config { get; set; }

    public override void Validate()
    {
        Name = Clean(Name, 60);
        ProfileName = OverlayStore.CleanProfileName(ProfileName);
        if (Config.ValueKind != JsonValueKind.Object)
            throw new ShareFormatException("Die Overlay-Einstellungen sind leer oder beschädigt.");
        if (Config.GetRawText().Length > ShareValidation.MaxOverlayChars)
            throw new ShareFormatException("Die Overlay-Einstellungen sind ungewöhnlich groß.");
    }
}

public enum SharedContentKind
{
    Mod,
    ResourcePack,
    Shader,
    Modpack
}

public sealed class ContentPayload : SharePayload
{
    public ContentPayload() => Format = ShareKinds.FormatOf(ShareKinds.Content);

    public SharedContentKind Kind { get; set; }
    public string ProjectId { get; set; } = "";
    public string? VersionId { get; set; }
    public string Title { get; set; } = "";

    public string KindText => InstallType is { } type ? ContentTypes.Label(type) : "Modpack";

    public ContentType? InstallType => Kind switch
    {
        SharedContentKind.Mod => ContentType.Mod,
        SharedContentKind.ResourcePack => ContentType.ResourcePack,
        SharedContentKind.Shader => ContentType.Shader,
        _ => null
    };

    public static ContentPayload Of(ContentType type, InstalledContent entry) => new()
    {
        Kind = type switch
        {
            ContentType.Mod => SharedContentKind.Mod,
            ContentType.Shader => SharedContentKind.Shader,
            _ => SharedContentKind.ResourcePack
        },
        ProjectId = entry.ProjectId,
        VersionId = entry.VersionId,
        Title = entry.Title
    };

    public override void Validate()
    {
        if (!Enum.IsDefined(Kind) || !ShareValidation.IsModrinthId(ProjectId))
            throw new ShareFormatException("Der geteilte Inhalt hat keine gültige Modrinth-Kennung.");
        if (VersionId != null && !ShareValidation.IsModrinthId(VersionId))
            VersionId = null;
        Title = Clean(Title, 100, ProjectId);
    }
}

public sealed class ServerPayload : SharePayload
{
    public ServerPayload() => Format = ShareKinds.FormatOf(ShareKinds.Server);

    public string Name { get; set; } = "";
    public string Address { get; set; } = "";

    public override void Validate()
    {
        Address = Address?.Trim() ?? "";
        if (!ShareValidation.IsServerAddress(Address))
            throw new ShareFormatException("Die Serveradresse ist ungültig.");
        Name = Clean(Name, 64, Address);
    }
}
