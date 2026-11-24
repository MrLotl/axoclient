using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface INbtReader
    {
        NbtCompound Read(Stream input);

        NbtCompound ReadFile(string path);

        /// <summary>servers.dat ist unkomprimiert, level.dat GZip. Atomar schreiben.</summary>
        void WriteFile(string path, NbtCompound root, bool gzip = false);
    }
}
