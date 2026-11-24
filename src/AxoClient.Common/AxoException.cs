namespace AxoClient.Common
{
    /// <summary>Ein erwarteter Fehler mit übersetzbarem Code. Alles andere gilt als unerwartet.</summary>
    public class AxoException : Exception
    {
        public AxoException(string code, Dictionary<string, object?>? parameters = null, Exception? inner = null)
            : base(code, inner)
        {
            Text = new TextRef(code, parameters);
        }

        public AxoException(TextRef text, Exception? inner = null)
            : base(text.Code, inner)
        {
            Text = text;
        }

        public TextRef Text { get; }
        public string Code => Text.Code;
    }
}
