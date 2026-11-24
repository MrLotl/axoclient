using System.IO.Abstractions.TestingHelpers;
using AxoClient.Common;
using AxoClient.DataModel.Settings;
using AxoClient.Settings;
using AxoClient.Settings.Contract;
using AxoClient.Social;
using AxoClient.Social.Contract;

namespace AxoClient.Tests
{
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
            var store = new JsonStore<TestDoc>(new MockFileSystem(), Path, new JsonHelper(), new AppLog(new LogRedactor()));
            var doc = await store.Load();
            Assert.Equal("", doc.Name);
        }

        [Fact]
        public async Task SaveAndLoadRoundTrip()
        {
            var fs = new MockFileSystem();
            var store = new JsonStore<TestDoc>(fs, Path, new JsonHelper(), new AppLog(new LogRedactor()));
            await store.Save(new TestDoc { Name = "Survival", Count = 3 });
            var doc = await new JsonStore<TestDoc>(fs, Path, new JsonHelper(), new AppLog(new LogRedactor())).Load();
            Assert.Equal("Survival", doc.Name);
            Assert.Equal(3, doc.Count);
            Assert.False(fs.File.Exists(Path + ".tmp"));
        }

        [Fact]
        public async Task BrokenFileIsMovedAside()
        {
            var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new("{ kaputt") });
            var store = new JsonStore<TestDoc>(fs, Path, new JsonHelper(), new AppLog(new LogRedactor()));
            var doc = await store.Load();
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
            var store = new JsonStore<TestDoc>(fs, Path, new JsonHelper(), new AppLog(new LogRedactor()), 2, [new JsonMigration(1, o =>
            {
                o["name"] = o["title"]?.GetValue<string>();
                o.Remove("title");
            })]);
            var doc = await store.Load();
            Assert.Equal("Alt", doc.Name);
            Assert.Equal(2, doc.FormatVersion);
        }
    }

    public class LogRedactorTests
    {
        private readonly ILogRedactor _logRedactor;
        public LogRedactorTests()
        {
            var services = new TestServices();
            _logRedactor = services.Get<ILogRedactor>();
        }

        [Fact]
        public void RemovesAccessToken()
        {
            var text = _logRedactor.Redact("java --accessToken eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.abcDEF123 --version 1.21");
            Assert.DoesNotContain("eyJhbGci", text);
            Assert.Contains("--version 1.21", text);
        }

        [Fact]
        public void RemovesBearerToken()
        {
            Assert.DoesNotContain("secret123", _logRedactor.Redact("Authorization: Bearer secret123"));
        }

        [Fact]
        public void SharingHidesUuidIpAndName()
        {
            _logRedactor.RegisterPlayerName("Granulator444");
            var text = _logRedactor.RedactForSharing(
                "Granulator444 (069a79f4-44e9-4726-a5be-fca90e38aaf5) joined from 84.12.3.4, local 127.0.0.1");
            Assert.DoesNotContain("Granulator444", text);
            Assert.DoesNotContain("069a79f4", text);
            Assert.DoesNotContain("84.12.3.4", text);
            Assert.Contains("127.0.0.1", text);
        }

        [Fact]
        public void NameInsideLongerWordStays()
        {
            _logRedactor.RegisterPlayerName("Steve");
            Assert.Contains("Steven", _logRedactor.RedactForSharing("Steven"));
        }
    }

    public class PathGuardTests
    {
        private readonly IPathGuard _pathGuard;
        public PathGuardTests()
        {
            var services = new TestServices();
            _pathGuard = services.Get<IPathGuard>();
        }

        [Theory]
        [InlineData(@"..\evil.txt")]
        [InlineData(@"a\..\..\evil.txt")]
        [InlineData(@"C:\Windows\system32")]
        public void RejectsEscapes(string relative)
        {
            Assert.Null(_pathGuard.SafeCombine(@"C:\base", relative));
        }

        [Fact]
        public void AllowsNestedPaths()
        {
            Assert.Equal(@"C:\base\mods\a.jar", _pathGuard.SafeCombine(@"C:\base", @"mods\a.jar"));
        }

        [Fact]
        public void PrefixIsNotInside()
        {
            Assert.False(_pathGuard.IsInside(@"C:\base", @"C:\base2\file"));
            Assert.True(_pathGuard.IsInside(@"C:\base", @"C:\base\file"));
        }

        [Theory]
        [InlineData("welt.zip", true)]
        [InlineData("con", false)]
        [InlineData("a/b", false)]
        [InlineData("..", false)]
        [InlineData("", false)]
        public void SafeFileNames(string name, bool expected)
        {
            Assert.Equal(expected, _pathGuard.IsSafeFileName(name));
        }
    }

    public class TextTests
    {
        private readonly ITextHelper _textHelper;
        public TextTests()
        {
            var services = new TestServices();
            _textHelper = services.Get<ITextHelper>();
        }

        [Fact]
        public void NewIdIsValidSlug()
        {
            var id = _textHelper.NewId("Mein Überleben!");
            Assert.True(_textHelper.IsValidId(id));
            Assert.StartsWith("mein-", id);
        }

        [Theory]
        [InlineData("../x", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("survival-1a2b", true)]
        public void IdValidation(string? id, bool expected)
        {
            Assert.Equal(expected, _textHelper.IsValidId(id));
        }

        [Fact]
        public void CleanTrimsAndCuts()
        {
            Assert.Equal("abc", _textHelper.Clean("  abc\u0000 ", 10));
            Assert.Equal(3, _textHelper.Clean("abcdef", 3).Length);
        }
    }

    public class AppearanceTests
    {
        private readonly IAppearance _appearance;
        public AppearanceTests()
        {
            var services = new TestServices();
            _appearance = services.Get<IAppearance>();
        }

        [Fact]
        public void OldCustomBecomesSavedGradient()
        {
            var a = new AppearanceSettings { Preset = "custom", Accent = new AccentGradient("#112233", "#445566", 90) };
            _appearance.Validate(a);
            var saved = Assert.Single(a.CustomGradients);
            Assert.Equal(Appearance.CustomPrefix + saved.Id, a.Preset);
            Assert.Equal(new AccentGradient("#112233", "#445566", 90), a.Accent);
        }

        [Fact]
        public void AccentFollowsSelectedCustomGradient()
        {
            var a = new AppearanceSettings
            {
                Preset = "custom:abc",
                Accent = new AccentGradient("#000000", "#000000", 0),
                CustomGradients = [new CustomGradient("abc", "#ff0000", "#00ff00", 400), new CustomGradient("bad", "red", "#00ff00", 0)]
            };
            _appearance.Validate(a);
            Assert.Single(a.CustomGradients);
            Assert.Equal(new AccentGradient("#ff0000", "#00ff00", 360), a.Accent);
        }

        [Fact]
        public void MissingCustomGradientFallsBackToAxo()
        {
            var a = new AppearanceSettings { Preset = "custom:gone" };
            _appearance.Validate(a);
            Assert.Equal("axo", a.Preset);
            Assert.Equal(_appearance.Presets["axo"], a.Accent);
        }
    }

    public class EmoteOptionTests
    {
        private readonly EmoteLibrary _emotes = (EmoteLibrary)new TestServices().Get<IEmoteLibrary>();

        private System.Text.Json.Nodes.JsonObject parse(string json) =>
            (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!;

        [Fact]
        public void BedrockArmsOnlyWithLoop()
        {
            var anim = parse("""{"animations":{"a":{"loop":true,"bones":{"right_arm":{},"head":{}}}}}""");
            Assert.Equal((true, true), _emotes.Detect(anim));
        }

        [Fact]
        public void BedrockWithLegsIsNotMoving()
        {
            var anim = parse("""{"animations":{"a":{"loop":"hold_on_last_frame","bones":{"leftLeg":{}}}}}""");
            Assert.Equal((true, false), _emotes.Detect(anim));
        }

        [Fact]
        public void EmotecraftLoopAsString()
        {
            var anim = parse("""{"emote":{"isLoop":"true","moves":[{"tick":0,"rightArm":{"pitch":1}}]}}""");
            Assert.Equal((true, true), _emotes.Detect(anim));
        }

        [Fact]
        public void ApplyLoopKeepsHoldAndRemovesWhenOff()
        {
            var hold = parse("""{"animations":{"a":{"loop":"hold_on_last_frame"}}}""");
            _emotes.ApplyLoop(hold, true);
            Assert.Equal("hold_on_last_frame", hold["animations"]!["a"]!["loop"]!.GetValue<string>());

            var plain = parse("""{"animations":{"a":{}}}""");
            _emotes.ApplyLoop(plain, true);
            Assert.True(plain["animations"]!["a"]!["loop"]!.GetValue<bool>());
            _emotes.ApplyLoop(plain, false);
            Assert.Null(plain["animations"]!["a"]!["loop"]);
        }
    }
}
