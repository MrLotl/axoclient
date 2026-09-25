namespace AxoClient.Instances;

public record InstanceResult(Installation Instance, List<string> Report);

public sealed class InstanceRegistry(LauncherSettings settings, Action save)
{
    public event Action? Changed;

    public List<Installation> All => settings.Installations;

    public Installation? Selected =>
        All.FirstOrDefault(i => i.Id == settings.SelectedInstallationId) ?? All.FirstOrDefault();

    public void Select(Installation inst)
    {
        settings.SelectedInstallationId = inst.Id;
        NotifyChanged();
    }

    public void NotifyChanged()
    {
        save();
        Changed?.Invoke();
    }

    public void Add(Installation inst)
    {
        if (inst.GameDir.Length == 0)
            inst.GameDir = NewGameDir(inst.Name, inst.Id);
        All.Add(inst);
        NotifyChanged();
    }

    public void Remove(Installation inst)
    {
        All.Remove(inst);
        InstanceIcons.Remove(inst);
        if (settings.SelectedInstallationId == inst.Id && All.Count > 0)
            settings.SelectedInstallationId = All[0].Id;
        NotifyChanged();
    }

    public string UniqueName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "Importierte Instanz";
        var candidate = name;
        for (var n = 2; All.Any(i => string.Equals(i.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }

    public async Task<Installation> CreateAsync(string name, LoaderType loader, string minecraftVersion,
        string? loaderVersion, Func<Installation, Task> populate)
    {
        var inst = new Installation
        {
            Name = UniqueName(name),
            Loader = loader,
            MinecraftVersion = minecraftVersion,
            LoaderVersion = loaderVersion
        };
        inst.GameDir = NewGameDir(inst.Name, inst.Id);
        try
        {
            Directory.CreateDirectory(inst.GameDir);
            await populate(inst);
        }
        catch
        {
            FileOps.TryDelete(inst.GameDir);
            throw;
        }
        Add(inst);
        return inst;
    }

    public static string NewGameDir(string name, string id) =>
        Path.Combine(AppPaths.Instances, $"{Sanitize.FileName(name, "Instanz")}-{id}");
}
