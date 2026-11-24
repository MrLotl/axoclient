namespace AxoClient.DataModel.Skins
{
    public sealed record MojangProfile(string Uuid, string Name, string? SkinUrl, SkinModel Model, IReadOnlyList<MojangCape> Capes);
}
