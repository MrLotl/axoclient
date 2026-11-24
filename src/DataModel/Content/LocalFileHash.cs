namespace AxoClient.DataModel.Content
{
    /// <summary>Hash einer lokalen Datei; CurseForge braucht zusätzlich den Murmur2-Fingerprint.</summary>
    public sealed record LocalFileHash(string Sha1, uint? Fingerprint);
}
