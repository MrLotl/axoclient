using AxoClient.DataModel.Import;

namespace AxoClient.App.Bridge.Requests
{
    public sealed record ForeignImportData(List<ForeignImportChoice> Choices);

    public sealed record FileRefData(string FileRef);

    public sealed record TransferData(string SourceId, string TargetId, ForeignImportParts Parts);
}
