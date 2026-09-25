namespace AxoClient.Game;

public record JvmPreset(string Id, string Name, string Description, string Arguments)
{
    public override string ToString() => Name;
}

public static class JvmPresets
{
    public const string BalancedId = "balanced";
    public const string ManyModsId = "manymods";
    public const string LowEndId = "lowend";
    public const string CustomId = "custom";

    public static readonly IReadOnlyList<JvmPreset> All =
    [
        new(BalancedId, "Ausgewogen",
            "Gute Voreinstellung für die meisten Rechner: kurze Ruckler beim Aufräumen, normaler Speicherbedarf.",
            "-XX:+UseG1GC -XX:MaxGCPauseMillis=50 -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 " +
            "-XX:G1HeapRegionSize=32M -XX:+ParallelRefProcEnabled -XX:+UnlockExperimentalVMOptions " +
            "-XX:+AlwaysPreTouch -XX:+DisableExplicitGC"),

        new(ManyModsId, "Viele Mods",
            "Für große Modpacks mit viel Arbeitsspeicher (8 GB und mehr): räumt in größeren Blöcken auf.",
            "-XX:+UseG1GC -XX:MaxGCPauseMillis=130 -XX:G1NewSizePercent=28 -XX:G1ReservePercent=20 " +
            "-XX:G1HeapRegionSize=16M -XX:G1MixedGCCountTarget=3 -XX:InitiatingHeapOccupancyPercent=20 " +
            "-XX:+ParallelRefProcEnabled -XX:+UnlockExperimentalVMOptions -XX:+AlwaysPreTouch " +
            "-XX:+DisableExplicitGC -XX:+PerfDisableSharedMem"),

        new(LowEndId, "Schwacher PC",
            "Für wenig Arbeitsspeicher (4 GB und weniger): räumt mit einem einzelnen Thread auf, das kostet " +
            "weniger Speicher und weniger CPU nebenher.",
            "-XX:+UseSerialGC -XX:MaxMetaspaceSize=256M -Xss1M"),

        new(CustomId, "Eigene",
            "Die Argumente im Feld darunter werden unverändert übergeben. Leer lassen heißt: nur die Standardwerte.",
            "")
    ];

    public static JvmPreset Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All.First(p => p.Id == CustomId);

    public static JvmPreset EffectiveFor(Installation? inst, LauncherSettings settings) =>
        Get(inst?.JvmPreset ?? settings.JvmPreset);

    public static string Resolve(LauncherSettings settings, Installation? inst = null)
    {
        var preset = EffectiveFor(inst, settings);
        var extra = (inst?.JvmArguments ?? settings.JvmArguments ?? "").Trim();
        if (preset.Id == CustomId)
            return extra;
        return extra.Length > 0 ? preset.Arguments + " " + extra : preset.Arguments;
    }

    public static string SuggestFor(int ramMb) => ramMb switch
    {
        >= 8192 => ManyModsId,
        <= 3072 => LowEndId,
        _ => BalancedId
    };
}
