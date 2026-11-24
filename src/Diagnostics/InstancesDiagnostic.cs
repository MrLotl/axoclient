using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.DataModel.Launch;
using AxoClient.Diagnostics.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Launch.Contract;

namespace AxoClient.Diagnostics
{
    /// <summary>Alle Prüfungen der Start-Pipeline für jede Instanz, ohne zu starten.</summary>
    public sealed class InstancesDiagnostic : IDiagnosticCheck
    {
        private readonly IInstanceManager _instances;
        private readonly ILaunchPipeline _pipeline;

        public InstancesDiagnostic(IInstanceManager instances, ILaunchPipeline pipeline)
        {
            _instances = instances;
            _pipeline = pipeline;
        }

        public string Area => "instances";

        public async Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken)
        {
            var items = new List<DiagnosticItem>();
            foreach (var instance in await _instances.LoadAll(cancellationToken))
            {
                var results = await _pipeline.Diagnose(instance.Id, cancellationToken);
                var problems = results.Where(result => result.Severity is Severity.Warning or Severity.Blocker).ToList();
                if (problems.Count == 0)
                {
                    items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.instanceOk", ("name", instance.Name))));
                }

                foreach (var problem in problems)
                {
                    items.Add(new(Area, problem.Severity == Severity.Blocker ? DiagnosticStatus.Error : DiagnosticStatus.Warning,
                        new TextRef("diagnostics.instanceIssue", ("name", instance.Name)), problem.Message, problem.Actions.FirstOrDefault()?.Label));
                }
            }
            return items;
        }
    }
}
