namespace AxoClient.Skins.Contract
{
    public interface ISkinFiles
    {
        /// <summary>Gültiger Skin: PNG, 64×64 oder 64×32.</summary>
        bool IsValidSkin(byte[] png);

        /// <summary>Gültiges Cape: PNG 64×32 oder ein Vielfaches bis 2048×1024, höchstens 1,5 MB.</summary>
        bool IsValidCape(byte[] png);

        string IdOf(byte[] png);
    }
}
