using AxoClient.Common;

namespace AxoClient.DataModel.Import
{
    public sealed record ForeignImportResult(string Name, string? InstanceId, IReadOnlyList<TextRef> Notes, TextRef? Error);
}
