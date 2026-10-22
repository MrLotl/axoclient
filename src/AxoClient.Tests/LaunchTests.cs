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
