namespace AxoClient.DataModel.Launch
{
    public sealed class CrashRule
    {
        public string Id { get; set; } = "";
        public List<string> Match { get; set; } = [];

        /// <summary><c>crash</c>, <c>hserr</c>, <c>log</c>; leer = überall.</summary>
        public List<string> In { get; set; } = [];
        public string Cause { get; set; } = "";
        public List<string> Actions { get; set; } = [];

        /// <summary><c>stacktrace</c>, <c>mixin</c>, <c>fabricResolution</c> oder <c>forgeDependency</c>.</summary>
        public string? FindMod { get; set; }
        public bool Server { get; set; }
    }
}
