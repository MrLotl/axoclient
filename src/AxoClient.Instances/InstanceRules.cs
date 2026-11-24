using AxoClient.Common;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    public class InstanceRules : IInstanceRules
    {
        private readonly ITextHelper _textHelper;
        public const int MaxNameLength = 60;

        public InstanceRules(ITextHelper textHelper)
        {
            _textHelper = textHelper;
        }

        public string CleanName(string? name)
        {
            var clean = _textHelper.Clean(name, MaxNameLength);
            return clean.Length == 0 ? throw new AxoException("instances.create.nameMissing") : clean;
        }
    }
}
