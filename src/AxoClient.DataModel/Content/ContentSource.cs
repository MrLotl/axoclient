using System.Text.Json.Serialization;

namespace AxoClient.DataModel.Content
{
    public enum ContentSource
    {
        Modrinth,
        [JsonStringEnumMemberName("curseforge")] CurseForge,
        Local,
        Friend,
        [JsonStringEnumMemberName("axoclient")] AxoClient
    }
}
