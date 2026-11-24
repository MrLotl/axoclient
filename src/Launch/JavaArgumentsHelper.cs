using System.Text;
using System.Text.RegularExpressions;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch
{
    /// <summary>Liest und prüft die eigenen Java-Argumente einer Instanz.</summary>
    public partial class JavaArgumentsHelper : IJavaArgumentsHelper
    {
        /// <summary>Enthalten die Argumente -Xmx/-Xms (der RAM-Regler hat Vorrang)?</summary>
        public bool HasMemoryArgument(string arguments)
        {
            return memoryArgumentPattern().IsMatch(arguments);
        }

        public string StripMemory(string arguments)
        {
            return string.Join(' ', Split(arguments).Where(argument => !memoryArgumentPattern().IsMatch(argument)));
        }

        /// <summary>Jedes Argument muss mit - beginnen, Anführungszeichen müssen paarig sein, keine Steuerzeichen.</summary>
        public bool IsValid(string arguments)
        {
            if (arguments.Count(character => character == '"') % 2 != 0 || arguments.Any(char.IsControl))
            {
                return false;
            }
            var parts = Split(arguments);
            return parts.Count > 0 && parts.All(part => part.StartsWith('-') && part.Length > 1)
                                   && !parts.Any(part => part.StartsWith("-jar", StringComparison.Ordinal) || part is "-cp" or "-classpath");
        }

        public List<string> Split(string arguments)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            foreach (var character in arguments)
            {
                if (character == '"')
                {
                    quoted = !quoted;
                }
                else if (char.IsWhiteSpace(character) && !quoted)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(character);
                }
            }
            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }
            return result;
        }

        [GeneratedRegex(@"(^|\s)-Xm[xs]\d+[kKmMgG]?\b")]
        private partial Regex memoryArgumentPattern();
    }
}
