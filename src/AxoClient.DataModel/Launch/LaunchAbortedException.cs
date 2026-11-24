namespace AxoClient.DataModel.Launch
{
    /// <summary>Bricht den Start bewusst ab (z. B. "Zum laufenden Spiel wechseln").</summary>
    public sealed class LaunchAbortedException : Exception
    {
        public LaunchAbortedException(string reason)
            : base(reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
