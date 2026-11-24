using System.IO.Compression;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Instances;
using AxoClient.Core.Launch;

namespace AxoClient.Tests;

/// <summary>Regeln für weitere häufige Absturzursachen, jeweils mit Maßnahme.</summary>
public class CrashRuleTests
{
    private static CrashAnalysis Analyze(string log, ContentDocument? content = null) =>
        new CrashAnalyzer().Analyze(new CrashInput(1, null, null, log, null), content);

    private static ContentEntry Mod(string file, string name, string id) => new()
    {
        File = file, Name = name, Enabled = true, Meta = new ModMetadata { ModId = id }
    };

    [Fact]
    public void FabricJavaRequirementIsWrongJavaNotModConflict()
    {
        var result = Analyze(string.Join('\n',
            "Incompatible mods found!",
            "net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!",
            " - Mod 'Minecraft' (minecraft) 1.21.4 requires version 21 or later of mod 'OpenJDK 64-Bit Server VM' (java), but only the wrong version is present: 17!"));
        Assert.Equal("wrongJavaFabric", result.RuleId);
        Assert.Equal("crash.cause.wrongJava", result.Cause.Code);
        Assert.Equal(["javaAuto"], result.Actions);
    }

    [Fact]
    public void FabricModInNeoForgeNamesTheFile()
    {
        var content = new ContentDocument { Mods = [Mod("sodium-fabric-0.6.jar", "Sodium", "sodium"), Mod("jei.jar", "JEI", "jei")] };
        var result = Analyze("[main/ERROR] [net.neoforged.fml.loading.moddiscovery.ModDiscoverer/]: File mods\\sodium-fabric-0.6.jar is a Fabric mod and cannot be loaded", content);
        Assert.Equal("wrongLoaderMod", result.RuleId);
        Assert.Equal("sodium-fabric-0.6.jar", result.ConfigFile);
        Assert.Equal(["Sodium"], result.Suspects.Select(s => s.Name).ToArray());
        Assert.Contains("disableMod", result.Actions);
    }

    [Fact]
    public void ForgeMissingDependencyBlamesTheRequestingMod()
    {
        var content = new ContentDocument { Mods = [Mod("rei.jar", "Roughly Enough Items", "roughlyenoughitems"), Mod("jei.jar", "JEI", "jei")] };
        var result = Analyze(string.Join('\n',
            "Missing or unsupported mandatory dependencies:",
            "\tMod ID: 'architectury', Requested by: 'roughlyenoughitems', Expected range: '[13.0.0,)', Actual version: '[MISSING]'"), content);
        Assert.Equal("forgeMissingDependency", result.RuleId);
        Assert.Equal(["Roughly Enough Items"], result.Suspects.Select(s => s.Name).ToArray());
        Assert.Equal("checkMods", result.Actions[0]);
    }

    [Fact]
    public void NewerNeoForgeDependencyMessageFindsModByName()
    {
        var content = new ContentDocument { Mods = [Mod("create.jar", "Create", "create")] };
        var result = Analyze("Mod Create requires Flywheel 1.0.0 or above\nCurrently, Flywheel is not installed", content);
        Assert.Equal("forgeMissingDependency", result.RuleId);
        Assert.Equal(["Create"], result.Suspects.Select(s => s.Name).ToArray());
    }

    [Theory]
    [InlineData("java.io.IOException: No space left on device")]
    [InlineData("java.io.IOException: There is not enough space on the disk")]
    [InlineData("java.io.IOException: Auf dem Datenträger ist nicht genügend Speicherplatz verfügbar")]
    public void DiskFullOffersCleanup(string log)
    {
        var result = Analyze(log);
        Assert.Equal("diskFull", result.RuleId);
        Assert.Equal(["cleanCache"], result.Actions);
    }

    [Fact]
    public void CorruptWorldPointsToBackups()
    {
        var result = Analyze("[Server thread/ERROR]: Exception reading C:\\x\\saves\\Neue Welt\\level.dat\njava.util.zip.ZipException: invalid distance too far back");
        Assert.Equal("worldCorrupt", result.RuleId);
        Assert.Equal(["openWorlds"], result.Actions);
    }
}

public class WorldBackupAllTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "axo-backup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public async Task BacksUpEveryWorldButSkipsOtherFolders()
    {
        var paths = new AppPaths(_root);
        var saves = Path.Combine(paths.GameDir("inst1"), "saves");
        foreach (var world in new[] { "Welt A", "Welt B" })
        {
            Directory.CreateDirectory(Path.Combine(saves, world, "region"));
            await File.WriteAllTextAsync(Path.Combine(saves, world, "level.dat"), "x");
            await File.WriteAllTextAsync(Path.Combine(saves, world, "region", "r.0.0.mca"), world);
        }
        Directory.CreateDirectory(Path.Combine(saves, "kein-level-dat"));

        var manager = new WorldManager(paths);
        var count = await manager.BackupAllAsync("inst1", JobContext.None());

        Assert.Equal(2, count);
        var backups = manager.ListBackups("inst1");
        Assert.Equal(2, backups.Count);
        using var zip = ZipFile.OpenRead(Path.Combine(paths.InstanceBackups("inst1"), backups.Single(b => b.File.StartsWith("Welt A")).File));
        Assert.Contains(zip.Entries, e => e.FullName == "Welt A/region/r.0.0.mca");
    }

    [Fact]
    public async Task NoWorldsMeansNoBackup()
    {
        var manager = new WorldManager(new AppPaths(_root));
        Assert.Equal(0, await manager.BackupAllAsync("inst1", JobContext.None()));
    }
}
