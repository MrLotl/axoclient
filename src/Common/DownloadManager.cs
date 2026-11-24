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

        public DownloadManager(IApiClient api, IFileSystem fileSystem, IAppLog appLog, int parallel = 6)
        {
            _api = api;
            _fs = fileSystem;
            _appLog = appLog;
            _parallel = parallel;
        }

        /// <summary>Lädt alle Dateien, meldet Fortschritt als Anteil der Bytes (bzw. Dateien, wenn Größen fehlen).</summary>
        public async Task DownloadAll(IReadOnlyCollection<DownloadRequest> requests, IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var todo = requests.Where(request => !isAlreadyValid(request)).ToList();
            if (todo.Count == 0)
            {
                progress?.Report(1);
                return;
            }

            var totalBytes = todo.All(downloadRequest => downloadRequest.Size is > 0) ? todo.Sum(downloadRequest => downloadRequest.Size!.Value) : 0;
            long doneBytes = 0;
            var doneFiles = 0;
            void Advance(long bytes, bool fileDone)
            {
                var total = Interlocked.Add(ref doneBytes, bytes);
                var files = fileDone ? Interlocked.Increment(ref doneFiles) : Volatile.Read(ref doneFiles);
                progress?.Report(totalBytes > 0 ? (double)total / totalBytes : (double)files / todo.Count);
            }

            await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = _parallel, CancellationToken = cancellationToken },
                async (request, token) =>
                {
                    await downloadOne(request, downloadedBytes => Advance(downloadedBytes, false), token);
                    Advance(0, true);
                });
            progress?.Report(1);
        }

        public Task Download(DownloadRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
            DownloadAll([request], progress, cancellationToken);

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

        private async Task downloadOne(DownloadRequest request, Action<long> onBytes, CancellationToken cancellationToken)
        {
            var directory = _fs.Path.GetDirectoryName(request.TargetPath)!;
            _fs.Directory.CreateDirectory(directory);
            var part = request.TargetPath + ".part";

            for (var attempt = 1; ; attempt++)
            {
                long received = 0;
                try
                {
                    using (var response = await _api.Send(() => new HttpRequestMessage(HttpMethod.Get, request.Url), cancellationToken,
                               completion: HttpCompletionOption.ResponseHeadersRead))
                    {
                        await _api.EnsureSuccess(response, cancellationToken);
                        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                        await using var target = _fs.File.Create(part);
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
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
                catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                           && attempt < Attempts && !cancellationToken.IsCancellationRequested)
                {
                    onBytes(-received);
                    tryDelete(part);
                    _appLog.Warn($"Download fehlgeschlagen (Versuch {attempt}): {request.Url}", exception);
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
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
