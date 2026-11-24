using AxoClient.DataModel.Game;

namespace AxoClient.DataModel.Content
{
    public sealed record SearchQuery(
        ProjectKind Kind,
        string Text,
        string? MinecraftVersion,
        LoaderType? Loader,
        string? Category,
        SearchSort Sort,
        int Page,
        int PageSize = 20);
}
