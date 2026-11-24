namespace AxoClient.DataModel.Social
{
    public sealed record RecipeContent(string Type, string Source, string ProjectId, string VersionId, bool Pack = false);
}
