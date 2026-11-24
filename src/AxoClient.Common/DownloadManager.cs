using System.IO.Abstractions;
using System.Security.Cryptography;

namespace AxoClient.Common
{
    /// <summary>
    /// Lädt Minecraft-Dateien, Mods, Java, Modpacks und Server-JARs parallel, mit Hash-Prüfung und Wiederholung.
    /// Eine Datei wird erst an ihren Platz verschoben, wenn der Hash stimmt (Sicherheit: manipulierte Dateien).
    /// </summary>
    public sealed class DownloadManager : IDownloadManager
    {
        private readonly IApiClient _api;
        private readonly IFileSystem _fs;
        private readonly IAppLog _appLog;
        private readonly int _parallel;
        private const int Attempts = 3;

        public DownloadManager(IApiClient api, IFileSystem fs, IAppLog appLog, int parallel = 6)
        {
            _api = api;
            _fs = fs;
            _appLog = appLog;
            _parallel = parallel;
        }

        /// <summary>Lädt alle Dateien, meldet Fortschritt als Anteil der Bytes (bzw. Dateien, wenn Größen fehlen).</summary>
        public async Task DownloadAll(IReadOnlyCollection<DownloadRequest> requests, IProgress<double>? progress,
            CancellationToken ct)
        {
            var todo = requests.Where(r => !isAlreadyValid(r)).ToList();
            if (todo.Count == 0)
            {
                progress?.Report(1);
                return;
            }

            var totalBytes = todo.All(r => r.Size is > 0) ? todo.Sum(r => r.Size!.Value) : 0;
            long doneBytes = 0;
            var doneFiles = 0;
            void Advance(long bytes, bool fileDone)
            {
                var total = Interlocked.Add(ref doneBytes, bytes);
                var files = fileDone ? Interlocked.Increment(ref doneFiles) : Volatile.Read(ref doneFiles);
                progress?.Report(totalBytes > 0 ? (double)total / totalBytes : (double)files / todo.Count);
            }

            await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = _parallel, CancellationToken = ct },
                async (request, token) =>
                {
                    await downloadOne(request, b => Advance(b, false), token);
                    Advance(0, true);
                });
            progress?.Report(1);
        }

        public Task Download(DownloadRequest request, IProgress<double>? progress, CancellationToken ct) =>
            DownloadAll([request], progress, ct);

        private bool isAlreadyValid(DownloadRequest request)
        {
            if (!_fs.File.Exists(request.TargetPath) || request.ExpectedHash == null)
            {
                return false;
            }

            if (request.Size is > 0 && _fs.FileInfo.New(request.TargetPath).Length != request.Size)
            {
                return false;
            }

            return HashMatches(request.TargetPath, request);
        }

        private async Task downloadOne(DownloadRequest request, Action<long> onBytes, CancellationToken ct)
        {
            var dir = _fs.Path.GetDirectoryName(request.TargetPath)!;
            _fs.Directory.CreateDirectory(dir);
            var part = request.TargetPath + ".part";

            for (var attempt = 1; ; attempt++)
            {
                long received = 0;
                try
                {
                    using (var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, request.Url), ct,
                               completion: HttpCompletionOption.ResponseHeadersRead))
                    {
                        await _api.EnsureSuccess(response, ct);
                        await using var source = await response.Content.ReadAsStreamAsync(ct);
                        await using var target = _fs.File.Create(part);
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, ct)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), ct);
                            received += read;
                            onBytes(read);
                        }
                    }

                    if (request.Size is > 0 && _fs.FileInfo.New(part).Length != request.Size)
                    {
                        throw new InvalidDataException("Größe stimmt nicht");
                    }

                    if (request.ExpectedHash != null && !HashMatches(part, request))
                    {
                        throw new InvalidDataException("Hash stimmt nicht");
                    }

                    _fs.File.Move(part, request.TargetPath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                           && attempt < Attempts && !ct.IsCancellationRequested)
                {
                    onBytes(-received);
                    tryDelete(part);
                    _appLog.Warn($"Download fehlgeschlagen (Versuch {attempt}): {request.Url}", ex);
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
                }
                catch (InvalidDataException)
                {
                    tryDelete(part);
                    throw new AxoException("errors.hashMismatch",
                        new() { ["file"] = _fs.Path.GetFileName(request.TargetPath) });
                }
                catch
                {
                    tryDelete(part);
                    throw;
                }
            }
        }

        public bool HashMatches(string path, DownloadRequest request)
        {
            using var stream = _fs.File.OpenRead(path);
            if (request.Sha512 != null)
            {
                return string.Equals(Convert.ToHexString(SHA512.HashData(stream)), request.Sha512, StringComparison.OrdinalIgnoreCase);
            }

            if (request.Sha256 != null)
            {
                return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), request.Sha256, StringComparison.OrdinalIgnoreCase);
            }

            if (request.Sha1 != null)
            {
                return string.Equals(Convert.ToHexString(SHA1.HashData(stream)), request.Sha1, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void tryDelete(string path)
        {
            try
            {
                if (_fs.File.Exists(path))
                {
                    _fs.File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
