namespace AxoClient.DataModel.Instances
{
    public sealed record ScreenshotInfo(string File, DateTimeOffset TakenAt, long SizeBytes, string Url, int? Width, int? Height);
}
