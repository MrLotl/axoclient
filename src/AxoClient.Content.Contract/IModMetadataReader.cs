using System.IO.Compression;
using AxoClient.DataModel.Content;

namespace AxoClient.Content.Contract
{
    public interface IModMetadataReader
    {
        /// <param name="preferLoader">
        /// Enthält die Jar Metadaten für mehrere Loader (z. B. Collective: Fabric und NeoForge), werden die für diesen Loader
        /// gelesen ("fabric", "quilt", "forge", "neoforge"). <see cref="ModMetadata.Loaders"/> nennt immer alle.
        /// </param>
        ModMetadataResult? Read(string jarPath, string? preferLoader = null);

        ModMetadataResult? Read(ZipArchive zip, string? preferLoader = null);
    }
}
