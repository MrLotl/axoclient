namespace AxoClient.Common
{
    /// <summary>
    /// Ein übersetzbarer Text: C# schickt nie fertige deutsche Sätze an die UI, sondern einen Code mit
    /// Parametern, z. B. <c>pipeline.ram.tooLow</c> mit <c>{ mb: 2048 }</c>. Die UI übersetzt über de.json.
    /// </summary>
    public sealed record TextRef(string Code, Dictionary<string, object?>? Params = null)
    {
        public TextRef(string code, params (string Key, object? Value)[] args)
            : this(code, args.Length == 0 ? null : args.ToDictionary(a => a.Key, a => a.Value))
        {
        }

        public override string ToString()
        {
            if (Params is { Count: > 0 })
            {
                return $"{Code} {string.Join(", ", Params.Select(p => $"{p.Key}={p.Value}"))}";
            }
            return Code;
        }
    }
}
