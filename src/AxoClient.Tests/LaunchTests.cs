using AxoClient.Core.Launch;

namespace AxoClient.Tests;

public class VersionRangeTests
{
    [Theory]
    [InlineData("1.21.1", ">=1.21", true)]
    [InlineData("1.20.4", ">=1.21", false)]
    [InlineData("1.21.4", ">=1.21 <1.21.5", true)]
    [InlineData("1.21.5", ">=1.21 <1.21.5", false)]
    [InlineData("1.20.1", "1.20.1 || 1.21", true)]
    [InlineData("1.21.1", "[1.21,1.22)", true)]
    [InlineData("1.22", "[1.21,1.22)", false)]
    [InlineData("47.2.0", "[47,)", true)]
    [InlineData("1.21", "", true)]
    [InlineData("1.21", null, true)]
    [InlineData("1.21.4", ">= 1.21.2", true)]
    [InlineData("1.21.1", ">= 1.21.2", false)]
    [InlineData("1.21.4", ">= 1.21 < 1.22", true)]
    public void Satisfies(string version, string? range, bool expected)
    {
        Assert.Equal(expected, VersionRange.Satisfies(version, range));
    }

    [Theory]
    [InlineData("1.21.10", "1.21.9")]
    [InlineData("1.21", "1.20.6")]
    [InlineData("0.16.5", "0.16.5-beta.1")]
    public void CompareOrdersNewerFirst(string newer, string older)
    {
        Assert.True(VersionRange.Compare(newer, older) > 0);
        Assert.True(VersionRange.Compare(older, newer) < 0);
    }
}

public class CrashAnalyzerTests
{
    private static CrashInput Log(string text) => new(1, null, null, text, null);

    [Fact]
    public void EmbeddedRulesLoadAndCompile()
    {
        var analyzer = new CrashAnalyzer();
        Assert.NotEmpty(analyzer.Rules);
        Assert.All(analyzer.Rules, r => Assert.False(string.IsNullOrEmpty(r.Cause)));
    }

    [Fact]
    public void ClientRuleIdsAreUnique()
    {
        var ids = CrashAnalyzer.LoadEmbeddedRules().Rules.Where(r => !r.Server).Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void DetectsOutOfMemory()
    {
        var result = new CrashAnalyzer().Analyze(Log("Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space"), null);
        Assert.True(result.KnownCause);
        Assert.Equal("outOfMemory", result.RuleId);
        Assert.Contains("increaseRam", result.Actions);
    }

    [Fact]
    public void UnknownCauseStaysUnknown()
    {
        var result = new CrashAnalyzer().Analyze(Log("Etwas völlig Unbekanntes ist passiert"), null);
        Assert.False(result.KnownCause);
        Assert.Null(result.RuleId);
    }
}

public class RepairIssueTests
{
    private static CheckResult Result(string check, Severity severity, string? key = null) =>
        new(check, severity, new AxoClient.Core.Common.TextRef("x"), [], Key: key);

    [Fact]
    public void SetupStepsAreNoRepair()
    {
        var doc = new AxoClient.Core.Content.ContentDocument();
        Assert.False(LaunchPipeline.IsRepairIssue(Result("joinRelay", Severity.AutoFix), doc));
        Assert.False(LaunchPipeline.IsRepairIssue(Result("unknownFiles", Severity.AutoFix), doc));
        Assert.False(LaunchPipeline.IsRepairIssue(Result("compat", Severity.AutoFix), doc));
        Assert.False(LaunchPipeline.IsRepairIssue(Result("memory", Severity.Warning, "ram-not-free"), doc));
        Assert.False(LaunchPipeline.IsRepairIssue(Result("compat", Severity.Ok), doc));
    }

    [Fact]
    public void RealProblemsCount()
    {
        var doc = new AxoClient.Core.Content.ContentDocument();
        Assert.True(LaunchPipeline.IsRepairIssue(Result("contentFiles", Severity.AutoFix), doc));
        Assert.True(LaunchPipeline.IsRepairIssue(Result("duplicate", Severity.Blocker), doc));
        Assert.True(LaunchPipeline.IsRepairIssue(Result("compat", Severity.Warning, "version-mismatch:x@1"), doc));
    }

    [Fact]
    public void SilencedWarningsAreHidden()
    {
        var doc = new AxoClient.Core.Content.ContentDocument();
        doc.IgnoredWarnings.Add("version-mismatch:x@1");
        Assert.False(LaunchPipeline.IsRepairIssue(Result("compat", Severity.Warning, "version-mismatch:x@1"), doc));
    }
}

public class AxoModInstallerTests
{
    [Fact]
    public void ReplacesJarWhenContentDiffers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"axomod-{Guid.NewGuid():N}.jar");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var sameHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
            var installer = new AxoClient.Core.Game.AxoModInstaller(typeof(AxoModInstallerTests).Assembly);
            Assert.True(installer.IsCurrent(new AxoClient.Core.Game.AxoModEntry { File = "x.jar", Sha256 = sameHash }, path));
            Assert.False(installer.IsCurrent(new AxoClient.Core.Game.AxoModEntry { File = "x.jar", Sha256 = new string('0', 64) }, path));
            Assert.False(installer.IsCurrent(new AxoClient.Core.Game.AxoModEntry { File = "x.jar", Sha256 = sameHash }, path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ContainerJarTests
{
    private static byte[] Zip(params (string Name, byte[] Data)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var (name, data) in files)
                using (var s = zip.CreateEntry(name).Open())
                    s.Write(data);
        return buffer.ToArray();
    }

    [Fact]
    public void ContainerJarProvidesNestedModIds()
    {
        // Wie Kotlin for Forge: außen keine Metadaten, innen eine Jar mit modId "kotlinforforge"
        var inner = Zip(("META-INF/neoforge.mods.toml", System.Text.Encoding.UTF8.GetBytes("[[mods]]\nmodId=\"kotlinforforge\"\n")));
        var outer = Zip(("META-INF/jarjar/kffmod.jar", inner));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(outer));
        var result = AxoClient.Core.Content.ModMetadataReader.Read(zip);
        Assert.NotNull(result);
        Assert.Contains("kotlinforforge", result!.Meta.Provides);
    }
}

public class MultiLoaderJarTests
{
    private static System.IO.Compression.ZipArchive Jar(params (string Name, string Text)[] files)
    {
        var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var (name, text) in files)
                using (var s = zip.CreateEntry(name).Open())
                    s.Write(System.Text.Encoding.UTF8.GetBytes(text));
        buffer.Position = 0;
        return new System.IO.Compression.ZipArchive(buffer);
    }

    [Fact]
    public void KnowsAllLoadersAndReadsPreferredOne()
    {
        // Wie Collective: Fabric- und NeoForge-Metadaten in einer Jar
        (string, string)[] files =
        [
            ("fabric.mod.json", "{\"id\":\"collective\",\"depends\":{\"minecraft\":\"1.21.1\"}}"),
            ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId=\"collective\"\n[[dependencies.collective]]\nmodId=\"minecraft\"\nversionRange=\"[1.21.1]\"\n")
        ];
        using var plain = Jar(files);
        var fabricFirst = AxoClient.Core.Content.ModMetadataReader.Read(plain)!;
        Assert.Contains("neoforge", fabricFirst.Meta.Loaders);
        Assert.Contains("fabric", fabricFirst.Meta.Loaders);

        using var again = Jar(files);
        var neo = AxoClient.Core.Content.ModMetadataReader.Read(again, "neoforge")!;
        Assert.Equal("neoforge", neo.Meta.Loaders[0]);
        Assert.Equal("[1.21.1]", neo.Meta.MinecraftRange);
    }

    [Fact]
    public void MinecraftRangeComesFromPrimaryMod()
    {
        using var jar = Jar(("META-INF/neoforge.mods.toml",
            "[[mods]]\nmodId=\"main\"\n[[mods]]\nmodId=\"extra\"\n" +
            "[[dependencies.main]]\nmodId=\"minecraft\"\nversionRange=\"[1.21.1,1.22)\"\n" +
            "[[dependencies.extra]]\nmodId=\"minecraft\"\nversionRange=\"[1.20.1,1.21.1)\"\n"));
        var meta = AxoClient.Core.Content.ModMetadataReader.Read(jar)!.Meta;
        Assert.Equal("[1.21.1,1.22)", meta.MinecraftRange);
        Assert.Contains("extra", meta.Provides);
    }
}

public class LooseVersionTests
{
    [Theory]
    [InlineData("1.21.1", "[1.21, 1.21.1)", true)]
    [InlineData("1.21.1", "[1.20.1,1.21.1)", true)]
    [InlineData("1.21.1", "[1.20,1.20.6]", false)]
    [InlineData("1.21.1", ">=1.21.2", true)]
    [InlineData("1.21.1", "1.20.x", false)]
    public void ToleratesSameLine(string version, string range, bool expected)
    {
        Assert.Equal(expected, VersionRange.SatisfiesLoosely(version, range));
    }
}
