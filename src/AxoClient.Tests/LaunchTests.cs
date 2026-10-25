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
