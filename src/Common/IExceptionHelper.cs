namespace AxoClient.Common
{
    public interface IExceptionHelper
    {
        /// <summary>Wandelt beliebige Fehler in einen Text für die UI um.</summary>
        TextRef Describe(Exception ex);
    }
}
