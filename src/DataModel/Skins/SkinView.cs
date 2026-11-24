namespace AxoClient.DataModel.Skins
{
    public sealed record SkinView(string Id, string Name, SkinModel Model, SkinOriginType Origin, string? Player, string Url, bool Active);
}
