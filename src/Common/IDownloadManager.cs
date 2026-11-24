namespace AxoClient.Common
{
    public interface IDownloadManager
    {
        /// <summary>Lädt alle Dateien, meldet Fortschritt als Anteil der Bytes (bzw. Dateien, wenn Größen fehlen).</summary>
        Task DownloadAll(IReadOnlyCollection<DownloadRequest> requests, IProgress<double>? progress,
        CancellationToken cancellationToken);

        Task Download(DownloadRequest request, IProgress<double>? progress, CancellationToken cancellationToken);

        bool HashMatches(string path, DownloadRequest request);
    }
}
