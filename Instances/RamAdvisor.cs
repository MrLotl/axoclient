namespace AxoClient.Instances;

public static class RamAdvisor
{
    public sealed record Advice(int Mb, string Reason);

    public static int TotalMb() => (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);

    public static int CurrentMb(Installation inst, LauncherSettings settings) => inst.MaxRamMb ?? settings.MaxRamMb;

    public static int SliderMaximum() => Math.Max(2048, TotalMb() / 512 * 512);

    public static Advice Recommend(Installation inst)
    {
        var mods = FileOps.CountEntries(inst.ModsDir, "*.jar");
        var shaders = FileOps.CountEntries(inst.ContentDir(ContentType.Shader));
        var packs = FileOps.CountEntries(inst.GameFile("resourcepacks"));

        var mb = inst.CanUseMods ? 3072 : 2048;
        var parts = new List<string> { inst.Loader.ToString() };
        if (mods > 0)
        {
            mb += Math.Min(mods * 35, 5120);
            parts.Add($"{mods} Mod{(mods == 1 ? "" : "s")}");
        }
        if (shaders > 0)
        {
            mb += 2048;
            parts.Add("Shader");
        }
        if (packs > 0)
        {
            mb += 512;
            parts.Add("Ressourcenpakete");
        }

        var cap = Math.Max(2048, TotalMb() * 6 / 10 / 512 * 512);
        mb = Math.Clamp((mb + 255) / 512 * 512, 2048, Math.Min(cap, 16384));
        return new Advice(mb, string.Join(", ", parts));
    }
}
