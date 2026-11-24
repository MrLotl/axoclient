using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AxoClient.Common;
using AxoClient.Game.Contract;

namespace AxoClient.Tests
{
    public class PanoramaTests
    {
        private const string Root = @"C:\axo";
        private readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[4000]];
        private readonly byte[] _placeholder = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[70]];

        private byte[] createJar(params (string Name, byte[] Data)[] entries)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, data) in entries)
                {
                    using var s = zip.CreateEntry(name).Open();
                    s.Write(data);
                }
            }
            return ms.ToArray();
        }

        private (IPanoramaService Service, MockFileSystem Fs, AppPaths Paths) setup()
        {
            var fs = new MockFileSystem();
            var paths = new AppPaths(Root, fs);
            return (new TestServices(Root, fs).Get<IPanoramaService>(), fs, paths);
        }

        [Fact]
        public void ReadsPanoramaFromVersionJar()
        {
            var (service, fs, paths) = setup();
            var jar = fs.Path.Combine(paths.Versions, "1.21.4", "1.21.4.jar");
            fs.AddFile(jar, new MockFileData(createJar(("assets/minecraft/textures/gui/title/background/panorama_0.png", _png))));

            Assert.Equal(_png, service.Get("1.21.4"));
        }

        [Fact]
        public void ReadsPanoramaFromAssetIndex()
        {
            var (service, fs, paths) = setup();
            const string hash = "f5e0a5cfbc2e7b1e89094d7882dde106b030ec26";
            fs.AddFile(fs.Path.Combine(paths.Versions, "1.21.1", "1.21.1.json"), new MockFileData("""{ "id": "1.21.1", "assetIndex": { "id": "17" } }"""));
            fs.AddFile(fs.Path.Combine(paths.Assets, "indexes", "17.json"), new MockFileData(
                """{ "objects": { "minecraft/textures/gui/title/background/panorama_0.png": { "hash": "f5e0a5cfbc2e7b1e89094d7882dde106b030ec26", "size": 4008 } } }"""));
            fs.AddFile(fs.Path.Combine(paths.Assets, "objects", hash[..2], hash), new MockFileData(_png));
            // Die Jar enthält nur den winzigen Platzhalter, der darf nicht gewinnen
            fs.AddFile(fs.Path.Combine(paths.Versions, "1.21.1", "1.21.1.jar"), new MockFileData(createJar(("assets/minecraft/textures/gui/title/background/panorama_0.png", _placeholder))));

            Assert.Equal(_png, service.Get("1.21.1"));
        }

        [Fact]
        public void PlaceholderInJarGivesNull()
        {
            var (service, fs, paths) = setup();
            fs.AddFile(fs.Path.Combine(paths.Versions, "26.2", "26.2.jar"), new MockFileData(createJar(("assets/minecraft/textures/gui/title/background/panorama_0.png", _placeholder))));
            Assert.Null(service.Get("26.2"));
        }

        [Fact]
        public void MissingJarOrEntryGivesNull()
        {
            var (service, fs, paths) = setup();
            Assert.Null(service.Get("1.21.4"));

            var jar = fs.Path.Combine(paths.Versions, "b1.7.3", "b1.7.3.jar");
            fs.AddFile(jar, new MockFileData(createJar(("terrain.png", _png))));
            Assert.Null(service.Get("b1.7.3"));
        }

        [Fact]
        public void NotAPngGivesNull()
        {
            var (service, fs, paths) = setup();
            var jar = fs.Path.Combine(paths.Versions, "1.20.1", "1.20.1.jar");
            fs.AddFile(jar, new MockFileData(createJar(("assets/minecraft/textures/gui/title/background/panorama_0.png", "kein Bild"u8.ToArray()))));
            Assert.Null(service.Get("1.20.1"));
        }

        [Fact]
        public void FoundAfterJarWasDownloaded()
        {
            var (service, fs, paths) = setup();
            Assert.Null(service.Get("1.21.4"));
            fs.AddFile(fs.Path.Combine(paths.Versions, "1.21.4", "1.21.4.jar"), new MockFileData(createJar(("assets/minecraft/textures/gui/title/background/panorama_0.png", _png))));
            Assert.NotNull(service.Get("1.21.4"));
        }

        [Theory]
        [InlineData("..")]
        [InlineData("../launcher")]
        [InlineData("1.21.4/../../x")]
        [InlineData("C:\\Windows")]
        [InlineData("")]
        public void RejectsUnsafeVersionNames(string version)
        {
            var (service, _, _) = setup();
            Assert.False(setup().Service.IsValidVersion(version));
            Assert.Null(service.Get(version));
        }

        [Theory]
        [InlineData("1.21.4")]
        [InlineData("24w14a")]
        [InlineData("1.20.1-pre2")]
        public void AcceptsNormalVersionNames(string version) => Assert.True(setup().Service.IsValidVersion(version));

        /// <summary>Nachgebauter Mojang-Server: Versionsliste, Versionsdatei, Asset-Index und Bild.</summary>
        private sealed class FakeMojang : HttpMessageHandler
        {
            private readonly byte[] _image;
            private readonly string? _overrideHash;
            private readonly bool _offline;
            public int Requests;

            public FakeMojang(byte[] image, string? overrideHash = null, bool offline = false)
            {
                _image = image;
                _overrideHash = overrideHash;
                _offline = offline;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Interlocked.Increment(ref Requests);
                if (_offline)
                {
                    throw new HttpRequestException("offline");
                }

                var url = request.RequestUri!.ToString();
                var hash = _overrideHash ?? Convert.ToHexStringLower(SHA1.HashData(_image));
                string? json = url switch
                {
                    _ when url.Contains("version_manifest") => """{ "latest": { "release": "26.3", "snapshot": "26.3" }, "versions": [ { "id": "26.3", "type": "release", "url": "https://piston-meta.mojang.com/v1/26.3.json", "releaseTime": "2026-11-01T00:00:00Z" } ] }""",
                    _ when url.EndsWith("/26.3.json") => """{ "assetIndex": { "id": "33", "url": "https://piston-meta.mojang.com/v1/33.json" } }""",
                    _ when url.EndsWith("/33.json") => $$"""{ "objects": { "minecraft/textures/gui/title/background/panorama_0.png": { "hash": "{{hash}}", "size": {{_image.Length}} } } }""",
                    _ => null
                };
                if (json != null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
                }

                if (url.StartsWith("https://resources.download.minecraft.net/", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_image) });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private (IPanoramaService Service, MockFileSystem Fs, AppPaths Paths) online(FakeMojang mojang, MockFileSystem? fs = null)
        {
            fs ??= new MockFileSystem();
            var paths = new AppPaths(Root, fs);
            var services = new TestServices(Root, fs, mojang);
            ((ApiClient)services.Get<IApiClient>()).NoRetryDelay = true;
            return (services.Get<IPanoramaService>(), fs, paths);
        }

        [Fact]
        public async Task DownloadsPanoramaOfNeverStartedVersion()
        {
            var mojang = new FakeMojang(_png);
            var (service, fs, paths) = online(mojang);

            Assert.Null(service.Get("26.3"));
            Assert.Equal(_png, await service.Fetch("26.3"));

            // liegt jetzt im Cache: ein neuer Dienst (z. B. nach einem Neustart) findet es ohne Internet
            IPanoramaService offline = new TestServices(Root, fs).Get<IPanoramaService>();
            Assert.Equal(_png, offline.Get("26.3"));
            Assert.True(fs.File.Exists(fs.Path.Combine(paths.Cache, "panoramas", "26.3_0.png")));
        }

        [Fact]
        public async Task RejectsImageWithWrongChecksum()
        {
            var (service, _, _) = online(new FakeMojang(_png, overrideHash: new string('a', 40)));
            Assert.Null(await service.Fetch("26.3"));
        }

        [Fact]
        public async Task UnknownVersionGivesNull()
        {
            var (service, _, _) = online(new FakeMojang(_png));
            Assert.Null(await service.Fetch("99.9"));
        }

        [Fact]
        public async Task OfflineGivesNullAndDoesNotRetryAtOnce()
        {
            var mojang = new FakeMojang(_png, offline: true);
            var (service, _, _) = online(mojang);
            Assert.Null(await service.Fetch("26.3"));
            var after = mojang.Requests;
            Assert.Null(await service.Fetch("26.3"));
            Assert.Equal(after, mojang.Requests);
        }

        [Fact]
        public void RejectsInvalidFace()
        {
            var (service, _, _) = setup();
            Assert.Null(service.Get("1.21.4", 6));
        }
    }
}
