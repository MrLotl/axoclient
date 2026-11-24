namespace AxoClient.App.Bridge.Requests
{
    public sealed record EdgeData(string Edge);

    public sealed record UrlData(string Url);

    public sealed record FolderData(string Kind, string? Id, string? Sub);

    public sealed record JobData(string Id);

    public sealed record OpenFileData(string Purpose, bool Multi = false);

    public sealed record SaveFileData(string Purpose, string? Name);

    public sealed record UploadData(string Name, string Base64);

    public sealed record TextData(string Text);
}
