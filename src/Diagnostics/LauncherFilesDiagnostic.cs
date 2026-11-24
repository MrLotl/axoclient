using System.Text.Json.Nodes;
using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;

namespace AxoClient.Diagnostics
{
    public sealed class LauncherFilesDiagnostic : IDiagnosticCheck
    {
        private readonly AppPaths _paths;

        public LauncherFilesDiagnostic(AppPaths paths)
        {
            _paths = paths;
        }

        public string Area => "launcher";

        public Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken ct)
        {
            var items = new List<DiagnosticItem>();
            var jsonFiles = new[] { _paths.LauncherJson, _paths.AccountsJson, _paths.StateJson, _paths.SkinLibrary, _paths.CapeLibrary }
                .Concat(Directory.Exists(_paths.Instances)
                    ? Directory.EnumerateDirectories(_paths.Instances).SelectMany(d => new[]
                        { Path.Combine(d, "instance.json"), Path.Combine(d, "content.json"), Path.Combine(d, "playtime.json") })
                    : [])
                .Concat(Directory.Exists(_paths.Servers)
                    ? Directory.EnumerateDirectories(_paths.Servers).SelectMany(d => new[] { Path.Combine(d, "server.json"), Path.Combine(d, "uptime.json") })
                    : [])
                .Where(File.Exists)
                .ToList();
            var broken = new List<string>();
            foreach (var file in jsonFiles)
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                    if (node?["formatVersion"]?.GetValue<int>() is not (>= 1 and <= 1))
                    {
                        broken.Add(Path.GetRelativePath(_paths.Root, file));
                    }
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or InvalidOperationException or FormatException)
                {
                    broken.Add(Path.GetRelativePath(_paths.Root, file));
                }
            }
            items.Add(broken.Count == 0
                ? new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.jsonOk", ("count", jsonFiles.Count)))
                : new(Area, DiagnosticStatus.Error, new TextRef("diagnostics.jsonBroken", ("files", string.Join(", ", broken.Take(5)))),
                    Solution: new TextRef("diagnostics.jsonBroken.solution")));

            var leftovers = Directory.EnumerateFiles(_paths.Root, "*.tmp", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Count();
            if (leftovers > 0)
            {
                items.Add(new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.tmpLeftovers", ("count", leftovers)),
                    Solution: new TextRef("diagnostics.tmpLeftovers.solution")));
            }

            return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
        }
    }
}
