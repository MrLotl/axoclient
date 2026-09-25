using System.Text.Json;

namespace AxoClient.Instances;

public class PackProfile
{
    public string Name { get; set; } = "";
    public List<string> ResourcePacks { get; set; } = [];
    public string? Shader { get; set; }
    public List<string> Servers { get; set; } = [];

    public string Summary
    {
        get
        {
            var packs = ResourcePacks.Count(p => !p.Equals("vanilla", StringComparison.OrdinalIgnoreCase));
            var parts = new List<string>
            {
                packs == 0 ? "ohne Ressourcenpaket" : Formats.Count(packs, "Ressourcenpaket", "Ressourcenpakete")
            };
            if (Shader is { Length: > 0 } shader)
                parts.Add("Shader: " + Path.GetFileNameWithoutExtension(shader));
            else if (Shader != null)
                parts.Add("Shader aus");
            parts.Add(Servers.Count > 0 ? "automatisch auf: " + string.Join(", ", Servers) : "nur von Hand");
            return string.Join("  ·  ", parts);
        }
    }
}

public class PackProfileStore(Installation inst)
{
    public const int MaxProfiles = 30;
    private const int MaxNameLength = 40;

    private string FilePath => inst.GameFile("launcher-packprofiles.json");

    public List<PackProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            return JsonSerializer.Deserialize<List<PackProfile>>(File.ReadAllText(FilePath))?
                       .Where(p => p is { Name.Length: > 0 })
                       .Take(MaxProfiles)
                       .ToList()
                   ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    public void Save(List<PackProfile> profiles) => JsonFiles.Write(FilePath, profiles.Take(MaxProfiles).ToList());

    public static string CleanName(string name) => Sanitize.Text(name, MaxNameLength);

    public List<string> CurrentResourcePacks() =>
        MinecraftOptions.ReadList(inst.OptionsFile, MinecraftOptions.ResourcePacks) ?? ["vanilla"];

    public string? CurrentShader() => IrisConfig.ReadShader(inst);

    public PackProfile Capture(string name) => new()
    {
        Name = CleanName(name),
        ResourcePacks = CurrentResourcePacks(),
        Shader = CurrentShader()
    };

    public List<string> Apply(PackProfile profile)
    {
        var report = new List<string>();

        var packs = profile.ResourcePacks.Count > 0 ? profile.ResourcePacks : ["vanilla"];
        if (MinecraftOptions.WriteList(inst.OptionsFile, MinecraftOptions.ResourcePacks, packs))
        {
            MinecraftOptions.WriteList(inst.OptionsFile, MinecraftOptions.IncompatibleResourcePacks, []);
            report.Add($"Ressourcenpakete gesetzt: {string.Join(", ", packs)}");
        }
        else
        {
            report.Add("Die options.txt konnte nicht geschrieben werden – starte das Spiel einmal, damit es sie anlegt.");
        }

        if (profile.Shader is { } shader)
        {
            if (IrisConfig.WriteShader(inst, shader))
                report.Add(shader.Length > 0 ? $"Shader gesetzt: {shader}" : "Shader ausgeschaltet");
            else
                report.Add("Die Shader-Einstellung konnte nicht geschrieben werden (ist Iris installiert?).");
        }
        return report;
    }

    public PackProfile? ForServer(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        return Load().FirstOrDefault(p =>
            p.Servers.Any(s => s.Trim().Equals(address.Trim(), StringComparison.OrdinalIgnoreCase)));
    }
}
