namespace AxoClient.Common
{
    public interface IDirectoryHelper
    {
        /// <summary>Größe eines Ordners samt Unterordnern in Bytes; 0, wenn er fehlt oder nicht lesbar ist.</summary>
        long Size(string directory);
    }
}
