using System.IO.Abstractions;
using System.Security.Cryptography;

namespace AxoClient.Core.Common;

/// <summary>Eine Datei, die geladen werden soll. Ohne Hash wird sie nur bei HTTPS akzeptiert und nie aus dem Cache gezogen.</summary>
public sealed record DownloadRequest(string Url, string TargetPath, string? Sha1 = null, string? Sha512 = null, long? Size = null)
{
    public string? ExpectedHash => Sha512 ?? Sha1;
}

/// <summary>
/// Lädt Minecraft-Dateien, Mods, Java, Modpacks und Server-JARs parallel, mit Hash-Prüfung und Wiederholung.
/// Eine Datei wird erst an ihren Platz verschoben, wenn der Hash stimmt (Sicherheit: manipulierte Dateien).
/// </summary>
public sealed class DownloadManager(ApiClient api, IFileSystem fs, int parallel = 6)
{
    private const int Attempts = 3;

    /// <summary>Lädt alle Dateien, meldet Fortschritt als Anteil der Bytes (bzw. Dateien, wenn Größen fehlen).</summary>
    public async Task DownloadAllAsync(IReadOnlyCollection<DownloadRequest> requests, IProgress<double>? progress,
        CancellationToken ct)
    {
        var todo = requests.Where(r => !IsAlreadyValid(r)).ToList();
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

        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct },
            async (request, token) =>
            {
                await DownloadOneAsync(request, b => Advance(b, false), token);
                Advance(0, true);
            });
        progress?.Report(1);
    }

    public Task DownloadAsync(DownloadRequest request, IProgress<double>? progress, CancellationToken ct) =>
        DownloadAllAsync([request], progress, ct);

    private bool IsAlreadyValid(DownloadRequest request)
    {
        if (!fs.File.Exists(request.TargetPath) || request.ExpectedHash == null)
            return false;
        if (request.Size is > 0 && fs.FileInfo.New(request.TargetPath).Length != request.Size)
            return false;
        return HashMatches(request.TargetPath, request);
    }

    private async Task DownloadOneAsync(DownloadRequest request, Action<long> onBytes, CancellationToken ct)
    {
        var dir = fs.Path.GetDirectoryName(request.TargetPath)!;
        fs.Directory.CreateDirectory(dir);
        var part = request.TargetPath + ".part";

        for (var attempt = 1;; attempt++)
        {
            long received = 0;
            try
            {
                using (var response = await api.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, request.Url), ct,
                           completion: HttpCompletionOption.ResponseHeadersRead))
                {
                    await ApiClient.EnsureSuccessAsync(response, ct);
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var target = fs.File.Create(part);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;
                        onBytes(read);
                    }
                }

                if (request.Size is > 0 && fs.FileInfo.New(part).Length != request.Size)
                    throw new InvalidDataException("Größe stimmt nicht");
                if (request.ExpectedHash != null && !HashMatches(part, request))
                    throw new InvalidDataException("Hash stimmt nicht");

                fs.File.Move(part, request.TargetPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                       && attempt < Attempts && !ct.IsCancellationRequested)
            {
                onBytes(-received);
                TryDelete(part);
                AppLog.Warn($"Download fehlgeschlagen (Versuch {attempt}): {request.Url}", ex);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
            catch (InvalidDataException)
            {
                TryDelete(part);
                throw new AxoException("errors.hashMismatch",
                    new() { ["file"] = fs.Path.GetFileName(request.TargetPath) });
            }
            catch
            {
                TryDelete(part);
                throw;
            }
        }
    }

    public bool HashMatches(string path, DownloadRequest request)
    {
        using var stream = fs.File.OpenRead(path);
        if (request.Sha512 != null)
            return string.Equals(Convert.ToHexString(SHA512.HashData(stream)), request.Sha512, StringComparison.OrdinalIgnoreCase);
        if (request.Sha1 != null)
            return string.Equals(Convert.ToHexString(SHA1.HashData(stream)), request.Sha1, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (fs.File.Exists(path))
                fs.File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
