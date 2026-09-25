using System.Text.Json;

namespace AxoClient.Instances;

public static class MinecraftOptions
{
    public const string ResourcePacks = "resourcePacks";
    public const string IncompatibleResourcePacks = "incompatibleResourcePacks";

    public static List<string>? ReadList(string optionsPath, string key)
    {
        var raw = ReadValue(optionsPath, key);
        if (raw == null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!).ToList()
                : null;
        }
        catch (JsonException ex)
        {
            ErrorReport.Log("options.txt lesen", ex);
            return null;
        }
    }

    public static string? ReadValue(string optionsPath, string key)
    {
        try
        {
            if (!File.Exists(optionsPath))
                return null;
            foreach (var line in File.ReadLines(optionsPath))
                if (line.StartsWith(key + ":", StringComparison.Ordinal))
                    return line[(key.Length + 1)..];
        }
        catch (IOException ex)
        {
            ErrorReport.Log("Spieloptionen lesen", ex);
        }
        return null;
    }

    public static bool WriteList(string optionsPath, string key, IReadOnlyList<string> values) =>
        WriteValue(optionsPath, key, JsonSerializer.Serialize(values));

    public static void WriteListOrThrow(string optionsPath, string key, IReadOnlyList<string> values)
    {
        if (!WriteList(optionsPath, key, values))
            throw new InvalidOperationException("Die options.txt konnte nicht geschrieben werden. Läuft Minecraft noch?");
    }

    public static bool WriteValue(string optionsPath, string key, string value)
    {
        try
        {
            if (!File.Exists(optionsPath))
                return false;
            var lines = File.ReadAllLines(optionsPath).ToList();
            var index = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
            var line = key + ":" + value;
            if (index >= 0)
                lines[index] = line;
            else
                lines.Add(line);
            File.WriteAllText(optionsPath, string.Join("\n", lines) + "\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string PackFileName(string entry) =>
        entry.StartsWith("file/", StringComparison.OrdinalIgnoreCase) ? entry["file/".Length..] : entry;
}

public static class IrisConfig
{
    private static string PathOf(Installation inst) => Path.Combine(inst.ConfigDir, "iris.properties");

    public static string? ReadShader(Installation inst)
    {
        var path = PathOf(inst);
        try
        {
            if (!File.Exists(path))
                return null;
            string? name = null;
            var enabled = true;
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("shaderPack=", StringComparison.Ordinal))
                    name = line["shaderPack=".Length..].Trim();
                else if (line.StartsWith("enableShaders=", StringComparison.Ordinal))
                    enabled = line["enableShaders=".Length..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            return enabled ? name ?? "" : "";
        }
        catch (IOException ex)
        {
            ErrorReport.Log("Shader-Einstellung lesen", ex);
            return null;
        }
    }

    public static bool WriteShader(Installation inst, string shader)
    {
        var path = PathOf(inst);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
            Set(lines, "shaderPack", shader);
            Set(lines, "enableShaders", shader.Length > 0 ? "true" : "false");
            File.WriteAllLines(path, lines);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Set(List<string> lines, string key, string value)
    {
        var index = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (index >= 0)
            lines[index] = key + "=" + value;
        else
            lines.Add(key + "=" + value);
    }
}
