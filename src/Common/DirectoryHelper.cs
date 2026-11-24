namespace AxoClient.Common
{
    /// <summary>Hilfsmethoden rund um Ordner auf der Platte.</summary>
    public class DirectoryHelper : IDirectoryHelper
    {
        /// <summary>Größe eines Ordners samt Unterordnern in Bytes; 0, wenn er fehlt oder nicht lesbar ist.</summary>
        public long Size(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return 0;
                }
                return new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }).Sum(file => file.Length);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
