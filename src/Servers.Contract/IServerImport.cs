using AxoClient.Common;
using AxoClient.DataModel.Servers;

namespace AxoClient.Servers.Contract
{
    public interface IServerImport
    {
        ServerImportInfo Inspect(string directory);

        /// <summary>Kopiert den Serverinhalt in den Datenordner des neuen Servers. Gibt die Zahl nicht kopierbarer Dateien zurück.</summary>
        int CopyInto(string from, string to, JobContext job);
    }
}
