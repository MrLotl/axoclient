namespace AxoClient.DataModel.Accounts
{
    public sealed class AccountCapes
    {
        /// <summary>ID des aktiven Mojang-Capes (nur zur Anzeige, die Wahrheit liegt bei Mojang).</summary>
        public string? Mojang { get; set; }
        public AxoCapeChoice? Axo { get; set; }
    }
}
