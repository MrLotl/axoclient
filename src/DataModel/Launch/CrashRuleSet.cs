namespace AxoClient.DataModel.Launch
{
    public sealed class CrashRuleSet
    {
        public int FormatVersion { get; set; } = 1;
        public List<CrashRule> Rules { get; set; } = [];
    }
}
