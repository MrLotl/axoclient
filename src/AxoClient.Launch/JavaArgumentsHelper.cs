using System.Text;
using System.Text.RegularExpressions;
using AxoClient.Launch.Contract;

namespace AxoClient.Launch
{
    /// <summary>Liest und prüft die eigenen Java-Argumente einer Instanz.</summary>
    public partial class JavaArgumentsHelper : IJavaArgumentsHelper
    {
        /// <summary>Enthalten die Argumente -Xmx/-Xms (der RAM-Regler hat Vorrang)?</summary>
        public bool HasMemoryArg(string args)
        {
            return memoryArg().IsMatch(args);
        }

        public string StripMemory(string args)
        {
            return string.Join(' ', Split(args).Where(a => !memoryArg().IsMatch(a)));
        }

        /// <summary>Jedes Argument muss mit - beginnen, Anführungszeichen müssen paarig sein, keine Steuerzeichen.</summary>
        public bool IsValid(string args)
        {
            if (args.Count(c => c == '"') % 2 != 0 || args.Any(char.IsControl))
            {
                return false;
            }
            var parts = Split(args);
            return parts.Count > 0 && parts.All(p => p.StartsWith('-') && p.Length > 1)
                                   && !parts.Any(p => p.StartsWith("-jar", StringComparison.Ordinal) || p is "-cp" or "-classpath");
        }

        public List<string> Split(string args)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            foreach (var c in args)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }
            return result;
        }

        [GeneratedRegex(@"(^|\s)-Xm[xs]\d+[kKmMgG]?\b")]
        private partial Regex memoryArg();
    }
}
