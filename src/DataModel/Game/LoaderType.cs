using System.Text.Json.Serialization;

namespace AxoClient.DataModel.Game
{
    public enum LoaderType
    {
        Vanilla,
        Fabric,
        Forge,
        [JsonStringEnumMemberName("neoforge")] NeoForge,
        Quilt
    }
}
