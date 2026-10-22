using System.IO.Abstractions.TestingHelpers;
using AxoClient.Core.Common;

namespace AxoClient.Tests;

public sealed class TestDoc : IVersionedDocument
{
    public int FormatVersion { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class JsonStoreTests
{
    private const string Path = @"C:\data\doc.json";

    [Fact]
    public async Task MissingFileGivesDefaults()
    {
        var store = new JsonStore<TestDoc>(new MockFileSystem(), Path);
        var doc = await store.LoadAsync();
        Assert.Equal("", doc.Name);
    }

    [Fact]
    public async Task SaveAndLoadRoundTrip()
    {
        var fs = new MockFileSystem();
        var store = new JsonStore<TestDoc>(fs, Path);
        await store.SaveAsync(new TestDoc { Name = "Survival", Count = 3 });
        var doc = await new JsonStore<TestDoc>(fs, Path).LoadAsync();
        Assert.Equal("Survival", doc.Name);
        Assert.Equal(3, doc.Count);
        Assert.False(fs.File.Exists(Path + ".tmp"));
    }

    [Fact]
    public async Task BrokenFileIsMovedAside()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new("{ kaputt") });
        var store = new JsonStore<TestDoc>(fs, Path);
        var doc = await store.LoadAsync();
        Assert.NotNull(doc);
        Assert.True(store.RecoveredFromBrokenFile);
    }

    [Fact]
    public async Task MigrationsRunInOrder()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path] = new("""{ "formatVersion": 1, "title": "Alt" }""")
        });
        var store = new JsonStore<TestDoc>(fs, Path, 2, [new JsonMigration(1, o =>
        {
            o["name"] = o["title"]?.GetValue<string>();
            o.Remove("title");
        })]);
        var doc = await store.LoadAsync();
        Assert.Equal("Alt", doc.Name);
        Assert.Equal(2, doc.FormatVersion);
    }
}

public class LogRedactorTests
{
    [Fact]
    public void RemovesAccessToken()
    {
        var text = LogRedactor.Redact("java --accessToken eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.abcDEF123 --version 1.21");
        Assert.DoesNotContain("eyJhbGci", text);
        Assert.Contains("--version 1.21", text);
    }

    [Fact]
    public void RemovesBearerToken()
    {
        Assert.DoesNotContain("secret123", LogRedactor.Redact("Authorization: Bearer secret123"));
    }

    [Fact]
    public void SharingHidesUuidIpAndName()
    {
        LogRedactor.RegisterPlayerName("Granulator444");
        var text = LogRedactor.RedactForSharing(
            "Granulator444 (069a79f4-44e9-4726-a5be-fca90e38aaf5) joined from 84.12.3.4, local 127.0.0.1");
        Assert.DoesNotContain("Granulator444", text);
        Assert.DoesNotContain("069a79f4", text);
        Assert.DoesNotContain("84.12.3.4", text);
        Assert.Contains("127.0.0.1", text);
    }

    [Fact]
    public void NameInsideLongerWordStays()
    {
        LogRedactor.RegisterPlayerName("Steve");
        Assert.Contains("Steven", LogRedactor.RedactForSharing("Steven"));
    }
}

public class PathGuardTests
{
    [Theory]
    [InlineData(@"..\evil.txt")]
    [InlineData(@"a\..\..\evil.txt")]
    [InlineData(@"C:\Windows\system32")]
    public void RejectsEscapes(string relative)
    {
        Assert.Null(PathGuard.SafeCombine(@"C:\base", relative));
    }

    [Fact]
    public void AllowsNestedPaths()
    {
        Assert.Equal(@"C:\base\mods\a.jar", PathGuard.SafeCombine(@"C:\base", @"mods\a.jar"));
    }

    [Fact]
    public void PrefixIsNotInside()
    {
        Assert.False(PathGuard.IsInside(@"C:\base", @"C:\base2\file"));
        Assert.True(PathGuard.IsInside(@"C:\base", @"C:\base\file"));
    }

    [Theory]
    [InlineData("welt.zip", true)]
    [InlineData("con", false)]
    [InlineData("a/b", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void SafeFileNames(string name, bool expected)
    {
        Assert.Equal(expected, PathGuard.IsSafeFileName(name));
    }
}

public class TextTests
{
    [Fact]
    public void NewIdIsValidSlug()
    {
        var id = Text.NewId("Mein Überleben!");
        Assert.True(Text.IsValidId(id));
        Assert.StartsWith("mein-", id);
    }

    [Theory]
    [InlineData("../x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("survival-1a2b", true)]
    public void IdValidation(string? id, bool expected)
    {
        Assert.Equal(expected, Text.IsValidId(id));
    }

    [Fact]
    public void CleanTrimsAndCuts()
    {
        Assert.Equal("abc", Text.Clean("  abc\u0000 ", 10));
        Assert.Equal(3, Text.Clean("abcdef", 3).Length);
    }
}
