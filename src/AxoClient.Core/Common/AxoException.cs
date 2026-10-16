namespace AxoClient.Core.Common;

/// <summary>
/// Ein übersetzbarer Text: C# schickt nie fertige deutsche Sätze an die UI, sondern einen Code mit
/// Parametern, z. B. <c>pipeline.ram.tooLow</c> mit <c>{ mb: 2048 }</c>. Die UI übersetzt über de.json.
/// </summary>
public sealed record TextRef(string Code, Dictionary<string, object?>? Params = null)
{
    public static TextRef Of(string code, params (string Key, object? Value)[] args) =>
        new(code, args.Length == 0 ? null : args.ToDictionary(a => a.Key, a => a.Value));

    public override string ToString() =>
        Params is { Count: > 0 } ? $"{Code} {string.Join(", ", Params.Select(p => $"{p.Key}={p.Value}"))}" : Code;
}

/// <summary>Ein erwarteter Fehler mit übersetzbarem Code. Alles andere gilt als unerwartet.</summary>
public class AxoException : Exception
{
    public AxoException(string code, Dictionary<string, object?>? parameters = null, Exception? inner = null)
        : base(code, inner)
    {
        Text = new TextRef(code, parameters);
    }

    public AxoException(TextRef text, Exception? inner = null) : base(text.Code, inner)
    {
        Text = text;
    }

    public TextRef Text { get; }
    public string Code => Text.Code;

    /// <summary>Wandelt beliebige Fehler in einen Text für die UI um.</summary>
    public static TextRef Describe(Exception ex) => ex switch
    {
        AxoException axo => axo.Text,
        OperationCanceledException => new TextRef("errors.cancelled"),
        HttpRequestException http when http.StatusCode is null => new TextRef("errors.offline"),
        HttpRequestException http => TextRef.Of("errors.http", ("status", (int)http.StatusCode!)),
        UnauthorizedAccessException => new TextRef("errors.accessDenied"),
        IOException io when io.HResult == unchecked((int)0x80070070) => new TextRef("errors.diskFull"),
        IOException => TextRef.Of("errors.io", ("detail", LogRedactor.Redact(ex.Message))),
        _ => TextRef.Of("errors.unexpected", ("detail", LogRedactor.Redact(ex.Message)))
    };
}
