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

        public async Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken)
        {
            var runtimes = await _java.FindAll(cancellationToken);
            if (runtimes.Count == 0)
            {
                return [new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.javaNone"))];
            }

            return runtimes.Select(runtime => runtime.Major > 0
                ? new DiagnosticItem(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.javaOk", ("major", runtime.Major), ("source", runtime.Source)))
                : new DiagnosticItem(Area, DiagnosticStatus.Error, new TextRef("diagnostics.javaBroken", ("source", runtime.Source)),
                    Solution: new TextRef("diagnostics.javaBroken.solution"))).ToList();
        }
    }
}
