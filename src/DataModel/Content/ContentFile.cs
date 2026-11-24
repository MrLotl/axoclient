namespace AxoClient.DataModel.Content
{
    public sealed record ContentFile(string Url, string FileName, string? Sha1, string? Sha512, long Size, bool Primary);
}
