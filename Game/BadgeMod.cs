using System.Reflection;
using System.Security.Cryptography;

namespace AxoClient.Game;

public static class BadgeMod
{
    public const string FileName = "mclauncher-badge.jar";

    private static readonly Dictionary<string, string> ModForVersion = new()
    {
        ["26.3"] = "26.3",
        ["26.2"] = "26.2",
        ["26.1"] = "26.1", ["26.1.1"] = "26.1", ["26.1.2"] = "26.1",
        ["1.21.11"] = "1.21.11", ["1.21.10"] = "1.21.11", ["1.21.9"] = "1.21.11",
        ["1.21.8"] = "1.21.8", ["1.21.7"] = "1.21.8", ["1.21.6"] = "1.21.8",
        ["1.21.5"] = "1.21.4", ["1.21.4"] = "1.21.4", ["1.21.3"] = "1.21.4", ["1.21.2"] = "1.21.4",
        ["1.21.1"] = "1.21.1", ["1.21"] = "1.21.1"
    };

    public static IEnumerable<string> SupportedVersions => ModForVersion
        .Where(entry => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(ResourceName(entry.Value)))
        .Select(entry => entry.Key);

    public static bool IsModFile(string path) => Path.GetFileName(path).StartsWith(FileName, StringComparison.OrdinalIgnoreCase);

    public static bool IsActiveFor(Installation inst, LauncherSettings settings) =>
        settings.BadgeEnabled && inst.Loader == LoaderType.Fabric && SupportedVersions.Contains(inst.MinecraftVersion);

    public static void Sync(Installation inst, LauncherSettings settings)
    {
        var target = Path.Combine(inst.ModsDir, FileName);
        if (!IsActiveFor(inst, settings))
        {
            if (File.Exists(target))
                File.Delete(target);
            return;
        }

        using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName(ModForVersion[inst.MinecraftVersion]))!;
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (File.Exists(target) && SHA1.HashData(File.ReadAllBytes(target)).SequenceEqual(SHA1.HashData(bytes)))
            return;
        Directory.CreateDirectory(inst.ModsDir);
        File.WriteAllBytes(target, bytes);
    }

    public static IEnumerable<string> JvmArguments() =>
    [
        $"-Dmclauncher.badge.api={AppInfo.AxoServiceUrl}",
        $"-Daxoclient.capes={AppInfo.ClientCapesUrl}"
    ];

    private static string ResourceName(string modVersion) => $"badge-mods/mclauncher-badge-{modVersion}.jar";
}
