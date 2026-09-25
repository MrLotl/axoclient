using System.Security.Cryptography;

namespace AxoClient.Core;

public static class HttpDownloads
{
    public static async Task<(string Sha1, string Sha512)> DownloadToFileAsync(HttpClient http, string url,
        string destination, Action<long>? bytesReceived, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    sha1.AppendData(buffer, 0, read);
                    sha512.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    bytesReceived?.Invoke(read);
                }
            }

            File.Move(partial, destination, overwrite: true);
            return (Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }
}
