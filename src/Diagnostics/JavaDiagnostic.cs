using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;
using AxoClient.Game.Contract;

namespace AxoClient.Diagnostics
{
    public sealed class JavaDiagnostic : IDiagnosticCheck
    {
        private readonly IJavaManager _java;

        public JavaDiagnostic(IJavaManager java)
        {
            _java = java;
        }

        public string Area => "java";

        public async Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken ct)
        {
            var runtimes = await _java.FindAll(ct);
            if (runtimes.Count == 0)
            {
                return [new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.javaNone"))];
            }

            return runtimes.Select(r => r.Major > 0
                ? new DiagnosticItem(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.javaOk", ("major", r.Major), ("source", r.Source)))
                : new DiagnosticItem(Area, DiagnosticStatus.Error, new TextRef("diagnostics.javaBroken", ("source", r.Source)),
                    Solution: new TextRef("diagnostics.javaBroken.solution"))).ToList();
        }
    }
}
