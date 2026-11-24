namespace AxoClient.DataModel.Social
{
    public sealed record Friend(string Uuid, string Name, Relation Relation, PresenceState State, long? Since, string? Join);
}
