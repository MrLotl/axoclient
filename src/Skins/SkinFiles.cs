using AxoClient.Common;
using AxoClient.Instances.Contract;
using AxoClient.Skins.Contract;

namespace AxoClient.Skins
{
    public class SkinFiles : ISkinFiles
    {
        private readonly ITextHelper _textHelper;
        private readonly IImageFormats _imageFormats;
        public const int MaxBytes = 256 * 1024;

        public SkinFiles(ITextHelper textHelper, IImageFormats imageFormats)
        {
            _textHelper = textHelper;
            _imageFormats = imageFormats;
        }

        /// <summary>Gültiger Skin: PNG, 64×64 oder 64×32.</summary>
        public bool IsValidSkin(byte[] png) =>
            png.Length <= MaxBytes && _imageFormats.PngSize(png) is (64, 64) or (64, 32);

        /// <summary>Gültiges Cape: PNG 64×32 oder ein Vielfaches bis 2048×1024, höchstens 1,5 MB.</summary>
        public bool IsValidCape(byte[] png)
        {
            if (png.Length > 1_572_864 || _imageFormats.PngSize(png) is not var (width, height))
            {
                return false;
            }

            return width * 32 == height * 64 && width >= 64 && width <= 2048 && (width & (width - 1)) == 0;
        }

        public string IdOf(byte[] png) => _textHelper.Sha256Hex(png)[..12];
    }
}
