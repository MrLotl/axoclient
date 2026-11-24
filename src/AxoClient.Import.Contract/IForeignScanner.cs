using AxoClient.Common;
using AxoClient.DataModel.Import;

namespace AxoClient.Import.Contract
{
    public interface IForeignScanner
    {
        /// <summary>Mod-Datei (auch deaktiviert)?</summary>
        bool IsModFile(string file);

        ForeignInstance? Get(string id);

        Task<IReadOnlyList<ForeignLauncherView>> Scan(JobContext job);

        /// <summary>Ordner (z. B. ein entpackter Export eines anderen Launchers) als Instanz lesen.</summary>
        ForeignInstance FromFolder(string root, string fallbackName);
    }
}
