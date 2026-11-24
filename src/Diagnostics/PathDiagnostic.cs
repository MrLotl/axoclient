using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;
using Microsoft.Win32;

namespace AxoClient.Diagnostics
{
    public sealed class PathDiagnostic : IDiagnosticCheck
    {
        private readonly AppPaths _paths;

        public PathDiagnostic(AppPaths paths)
        {
            _paths = paths;
        }

        public string Area => "paths";

        public Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken)
        {
            var items = new List<DiagnosticItem>();
            try
            {
                var probe = Path.Combine(_paths.Tmp, $"probe-{Guid.NewGuid():N}");
                Directory.CreateDirectory(_paths.Tmp);
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                items.Add(new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.writable")));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                items.Add(new(Area, DiagnosticStatus.Error, new TextRef("diagnostics.notWritable"), Solution: new TextRef("diagnostics.notWritable.solution")));
            }
            if (_paths.Root.Any(character => character > 127))
            {
                items.Add(new(Area, DiagnosticStatus.Warning, new TextRef("diagnostics.pathUmlauts"), Solution: new TextRef("diagnostics.pathUmlauts.solution")));
            }

            var longPaths = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem")?.GetValue("LongPathsEnabled") is 1;
            items.Add(new(Area, longPaths ? DiagnosticStatus.Ok : DiagnosticStatus.Warning, new TextRef(longPaths ? "diagnostics.longPaths" : "diagnostics.noLongPaths"),
                Solution: longPaths ? null : new TextRef("diagnostics.noLongPaths.solution")));
            return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
        }
    }
}
