namespace AxoClient.Common
{
    /// <summary>Wandelt beliebige Fehler in einen übersetzbaren Text für die UI um.</summary>
    public class ExceptionHelper : IExceptionHelper
    {
        private readonly ILogRedactor _logRedactor;

        public ExceptionHelper(ILogRedactor logRedactor)
        {
            _logRedactor = logRedactor;
        }

        public TextRef Describe(Exception exception)
        {
            return exception switch
            {
                AxoException axoException => axoException.Text,
                OperationCanceledException => new TextRef("errors.cancelled"),
                HttpRequestException http when http.StatusCode is null => new TextRef("errors.offline"),
                HttpRequestException http => new TextRef("errors.http", ("status", (int)http.StatusCode!)),
                UnauthorizedAccessException => new TextRef("errors.accessDenied"),
                IOException ioException when ioException.HResult == unchecked((int)0x80070070) => new TextRef("errors.diskFull"),
                IOException => new TextRef("errors.io", ("detail", _logRedactor.Redact(exception.Message))),
                _ => new TextRef("errors.unexpected", ("detail", _logRedactor.Redact(exception.Message)))
            };
        }
    }
}
