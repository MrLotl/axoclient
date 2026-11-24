namespace AxoClient.Common
{
    /// <summary>Eine Datei, die geladen werden soll. Ohne Hash wird sie nur bei HTTPS akzeptiert und nie aus dem Cache gezogen.</summary>
    public sealed record DownloadRequest(string Url, string TargetPath, string? Sha1 = null, string? Sha512 = null, long? Size = null,
        string? Sha256 = null)
    {
        public string? ExpectedHash => Sha512 ?? Sha256 ?? Sha1;
    }
}
