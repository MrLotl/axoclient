namespace AxoClient.Common
{
    /// <summary>Hilfsmethoden rund um Ordner auf der Platte.</summary>
    public class DirectoryHelper : IDirectoryHelper
    {
        /// <summary>Größe eines Ordners samt Unterordnern in Bytes; 0, wenn er fehlt oder nicht lesbar ist.</summary>
        public long Size(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return 0;
                }
                return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }).Sum(f => f.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
