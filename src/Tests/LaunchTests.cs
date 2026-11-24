using AxoClient.Common;
using AxoClient.Content.Contract;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.DataModel.Launch;
using AxoClient.Game;
using AxoClient.Game.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Tests
{
    public class VersionRangeTests
    {
        private readonly IVersionRange _versionRange;
        public VersionRangeTests()
        {
            var services = new TestServices();
            _versionRange = services.Get<IVersionRange>();
        }

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
            Assert.Equal(expected, _versionRange.Satisfies(version, range));
        }

        [Theory]
        [InlineData("1.21.10", "1.21.9")]
        [InlineData("1.21", "1.20.6")]
        [InlineData("0.16.5", "0.16.5-beta.1")]
        public void CompareOrdersNewerFirst(string newer, string older)
        {
            Assert.True(_versionRange.Compare(newer, older) > 0);
            Assert.True(_versionRange.Compare(older, newer) < 0);
        }
    }

    public class CrashAnalyzerTests
    {
        private readonly ICrashAnalyzer _crashAnalyzer;
        public CrashAnalyzerTests()
        {
            var services = new TestServices();
            _crashAnalyzer = services.Get<ICrashAnalyzer>();
        }

        private CrashInput log(string text) => new(1, null, null, text, null);

        [Fact]
        public void EmbeddedRulesLoadAndCompile()
        {
            ICrashAnalyzer analyzer = new TestServices().Get<ICrashAnalyzer>();
            Assert.NotEmpty(analyzer.Rules);
            Assert.All(analyzer.Rules, crashRule => Assert.False(string.IsNullOrEmpty(crashRule.Cause)));
        }

        [Fact]
        public void ClientRuleIdsAreUnique()
        {
            var ids = _crashAnalyzer.LoadEmbeddedRules().Rules.Where(rule => !rule.Server).Select(rule => rule.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void DetectsOutOfMemory()
        {
            var result = new TestServices().Get<ICrashAnalyzer>().Analyze(log("Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space"), null);
            Assert.True(result.KnownCause);
            Assert.Equal("outOfMemory", result.RuleId);
            Assert.Contains("increaseRam", result.Actions);
        }

        [Fact]
        public void UnknownCauseStaysUnknown()
        {
            var result = new TestServices().Get<ICrashAnalyzer>().Analyze(log("Etwas völlig Unbekanntes ist passiert"), null);
            Assert.False(result.KnownCause);
            Assert.Null(result.RuleId);
        }
    }

    public class RepairIssueTests
    {
        private readonly ILaunchPipeline _pipeline = new TestServices().Get<ILaunchPipeline>();

        private CheckResult result(string check, Severity severity, string? key = null) =>
            new(check, severity, new TextRef("x"), [], Key: key);

        [Fact]
        public void SetupStepsAreNoRepair()
        {
            var document = new ContentDocument();
            Assert.False(_pipeline.IsRepairIssue(result("joinRelay", Severity.AutoFix), document));
            Assert.False(_pipeline.IsRepairIssue(result("unknownFiles", Severity.AutoFix), document));
            Assert.False(_pipeline.IsRepairIssue(result("compat", Severity.AutoFix), document));
            Assert.False(_pipeline.IsRepairIssue(result("memory", Severity.Warning, "ram-not-free"), document));
            Assert.False(_pipeline.IsRepairIssue(result("compat", Severity.Ok), document));
        }

        [Fact]
        public void RealProblemsCount()
        {
            var document = new ContentDocument();
            Assert.True(_pipeline.IsRepairIssue(result("contentFiles", Severity.AutoFix), document));
            Assert.True(_pipeline.IsRepairIssue(result("duplicate", Severity.Blocker), document));
            Assert.True(_pipeline.IsRepairIssue(result("compat", Severity.Warning, "version-mismatch:x@1"), document));
        }

        [Fact]
        public void SilencedWarningsAreHidden()
        {
            var document = new ContentDocument();
            document.IgnoredWarnings.Add("version-mismatch:x@1");
            Assert.False(_pipeline.IsRepairIssue(result("compat", Severity.Warning, "version-mismatch:x@1"), document));
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
                IAxoModInstaller installer = new AxoModInstaller(typeof(AxoModInstallerTests).Assembly, new TextHelper(), new AppInfo(), new JsonHelper());
                Assert.True(installer.IsCurrent(new AxoModEntry { File = "x.jar", Sha256 = sameHash }, path));
                Assert.False(installer.IsCurrent(new AxoModEntry { File = "x.jar", Sha256 = new string('0', 64) }, path));
                Assert.False(installer.IsCurrent(new AxoModEntry { File = "x.jar", Sha256 = sameHash }, path + ".missing"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    public class ContainerJarTests
    {
        private readonly IModMetadataReader _modMetadataReader;
        public ContainerJarTests()
        {
            var services = new TestServices();
            _modMetadataReader = services.Get<IModMetadataReader>();
        }

        private byte[] createZip(params (string Name, byte[] Data)[] files)
        {
            using var buffer = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var (name, data) in files)
                {
                    using (var stream = zip.CreateEntry(name).Open())
                    {
                        stream.Write(data);
                    }
                }
            }

            return buffer.ToArray();
        }

        [Fact]
        public void ContainerJarProvidesNestedModIds()
        {
            // Wie Kotlin for Forge: außen keine Metadaten, innen eine Jar mit modId "kotlinforforge"
            var inner = createZip(("META-INF/neoforge.mods.toml", System.Text.Encoding.UTF8.GetBytes("[[mods]]\nmodId=\"kotlinforforge\"\n")));
            var outer = createZip(("META-INF/jarjar/kffmod.jar", inner));
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(outer));
            var result = _modMetadataReader.Read(zip);
            Assert.NotNull(result);
            Assert.Contains("kotlinforforge", result!.Meta.Provides);
        }
    }

    public class MultiLoaderJarTests
    {
        private readonly IModMetadataReader _modMetadataReader;
        public MultiLoaderJarTests()
        {
            var services = new TestServices();
            _modMetadataReader = services.Get<IModMetadataReader>();
        }

        private System.IO.Compression.ZipArchive createJar(params (string Name, string Text)[] files)
        {
            var buffer = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var (name, text) in files)
                {
                    using (var stream = zip.CreateEntry(name).Open())
                    {
                        stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
                    }
                }
            }

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
            using var plain = createJar(files);
            var fabricFirst = _modMetadataReader.Read(plain)!;
            Assert.Contains("neoforge", fabricFirst.Meta.Loaders);
            Assert.Contains("fabric", fabricFirst.Meta.Loaders);

            using var again = createJar(files);
            var neoForge = _modMetadataReader.Read(again, "neoforge")!;
            Assert.Equal("neoforge", neoForge.Meta.Loaders[0]);
            Assert.Equal("[1.21.1]", neoForge.Meta.MinecraftRange);
        }

        [Fact]
        public void MinecraftRangeComesFromPrimaryMod()
        {
            using var jar = createJar(("META-INF/neoforge.mods.toml",
                "[[mods]]\nmodId=\"main\"\n[[mods]]\nmodId=\"extra\"\n" +
                "[[dependencies.main]]\nmodId=\"minecraft\"\nversionRange=\"[1.21.1,1.22)\"\n" +
                "[[dependencies.extra]]\nmodId=\"minecraft\"\nversionRange=\"[1.20.1,1.21.1)\"\n"));
            var meta = _modMetadataReader.Read(jar)!.Meta;
            Assert.Equal("[1.21.1,1.22)", meta.MinecraftRange);
            Assert.Contains("extra", meta.Provides);
        }
    }

    public class LooseVersionTests
    {
        private readonly IVersionRange _versionRange;
        public LooseVersionTests()
        {
            var services = new TestServices();
            _versionRange = services.Get<IVersionRange>();
        }

        [Theory]
        [InlineData("1.21.1", "[1.21, 1.21.1)", true)]
        [InlineData("1.21.1", "[1.20.1,1.21.1)", true)]
        [InlineData("1.21.1", "[1.20,1.20.6]", false)]
        [InlineData("1.21.1", ">=1.21.2", true)]
        [InlineData("1.21.1", "1.20.x", false)]
        public void ToleratesSameLine(string version, string range, bool expected)
        {
            Assert.Equal(expected, _versionRange.SatisfiesLoosely(version, range));
        }
    }

    public class MixinConflictTests
    {
        private ContentEntry mod(string file, string name, string id, string mixins, params string[] provides) => new()
        {
            File = file,
            Name = name,
            Enabled = true,
            Meta = new ModMetadata { ModId = id, Mixins = [mixins], Provides = [.. provides] }
        };

        [Fact]
        public void NamesOnlyTheConflictingModsNotEveryModInTheLog()
        {
            // Gekürzt aus einem echten Log: Sable scheitert, weil Immersive Portals dieselbe Stelle schon verändert hat.
            // Davor nennt das Log beim normalen Laden die Mixin-Konfigurationen aller Mods.
            var log = string.Join('\n',
                "[19:35:36] [main/INFO] [mixin/]: Compatibility level set to JAVA_21 for create.mixins.json",
                "[19:35:36] [main/INFO] [mixin/]: Loaded jei.mixins.json and lithium.mixins.json",
                "[19:35:37] [main/WARN] [mixin/]: @Redirect conflict. Skipping imm_ptl.mixins.json:common.collision.MixinEntity from mod immersive_portals_core->@Redirect::redirectHandleCollisions with priority 1000, already redirected by sable.mixins.json:entity.EntityMixin from mod sable->@Redirect::sable$collideRedirect with priority 1100",
                "[19:35:37] [main/FATAL] [mixin/]: Mixin apply for mod sable failed sable.mixins.json:player_standup.PlayerMixin from mod sable -> net.minecraft.world.entity.player.Player: org.spongepowered.asm.mixin.injection.throwables.InvalidInjectionException cannot inject into Player::canPlayerFitWithinBlocksAndEntitiesWhen merged by qouteall.imm_ptl.core.mixin.common.collision.MixinPlayer_Collision with priority 1000",
                "Caused by: org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [sable.mixins.json:player_standup.PlayerMixin from mod sable] from phase [DEFAULT] in config [sable.mixins.json] FAILED during APPLY");
            var content = new ContentDocument
            {
                Mods =
                [
                    mod("create.jar", "Create", "create", "create.mixins.json"),
                    mod("jei.jar", "JEI", "jei", "jei.mixins.json"),
                    mod("lithium.jar", "Lithium", "lithium", "lithium.mixins.json"),
                    mod("immersive_portals-6.0.7-all.jar", "Immersive Portals", "immersive_portals", "ip.mixins.json", "immersive_portals_core"),
                    mod("sable.jar", "Sable", "sable", "sable.mixins.json")
                ]
            };
            var result = new TestServices().Get<ICrashAnalyzer>().Analyze(new CrashInput(1, null, null, log, null), content);
            Assert.Equal("mixin", result.RuleId);
            Assert.Equal(["Sable", "Immersive Portals"], result.Suspects.Select(suspect => suspect.Name).ToArray());
            Assert.Equal("crash.cause.mixinConflict", result.Cause.Code);
        }
    }
}
