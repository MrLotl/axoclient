using AxoClient.Accounts.Contract;
using AxoClient.Common;

namespace AxoClient.Accounts
{
    /// <summary>Spieler-UUIDs in einheitlicher Form.</summary>
    public class UuidHelper : IUuidHelper
    {
        /// <summary>Kompakte UUID (32 Zeichen) in die Form mit Bindestrichen bringen.</summary>
        public string FormatUuid(string uuid)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length == 32)
            {
                return $"{compact[..8]}-{compact[8..12]}-{compact[12..16]}-{compact[16..20]}-{compact[20..]}";
            }
            return uuid;
        }

        /// <summary>UUID für Adressen beim Dienst: kompakt, nur Hex-Zeichen, sonst Fehler.</summary>
        public string RequireUuid(string uuid)
        {
            var compact = uuid.Replace("-", "").ToLowerInvariant();
            if (compact.Length == 32 && compact.All(Uri.IsHexDigit))
            {
                return compact;
            }
            throw new AxoException("errors.invalidId");
        }
    }
}
