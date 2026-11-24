namespace AxoClient.DataModel.Content
{
    public enum ProjectKind
    {
        Mod,
        ResourcePack,
        ShaderPack,
        Modpack,

        /// <summary>Datapack für eine Welt (bei Modrinth Projekte mit dem Loader "datapack").</summary>
        DataPack
    }
}
