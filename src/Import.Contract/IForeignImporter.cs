using AxoClient.Common;
using AxoClient.DataModel.Import;

namespace AxoClient.Import.Contract
{
    public interface IForeignImporter
    {
        Task<IReadOnlyList<ForeignImportResult>> Import(IReadOnlyList<ForeignImportChoice> choices, JobContext job);
    }
}
