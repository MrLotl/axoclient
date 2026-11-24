namespace AxoClient.DataModel.Content
{
    public sealed record SearchPage(IReadOnlyList<ContentProject> Items, int Total);
}
