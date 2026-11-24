namespace AxoClient.Common
{
    /// <summary>Was die UI über einen laufenden Vorgang weiß.</summary>
    public sealed record JobInfo(
        string Id,
        string Kind,
        TextRef Title,
        JobState State,
        double? Progress,
        TextRef? Status,
        TextRef? Error,
        string? Subject);
}
