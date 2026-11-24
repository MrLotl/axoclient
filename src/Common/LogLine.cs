namespace AxoClient.Common
{
    public sealed record LogLine(long Seq, DateTimeOffset Time, LogLevel Level, string Thread, string Text, bool StdErr);
}
