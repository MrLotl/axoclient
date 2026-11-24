using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;
using Microsoft.Win32;

namespace AxoClient.Diagnostics
{
    public sealed class SystemDiagnostic : IDiagnosticCheck
    {
        private readonly AppPaths _paths;
        private readonly ISystemInfo _systemInfo;
        private readonly ITextHelper _textHelper;

        public SystemDiagnostic(AppPaths paths, ISystemInfo systemInfo, ITextHelper textHelper)
        {
            _paths = paths;
            _systemInfo = systemInfo;
            _textHelper = textHelper;
        }

        public string Area => "system";

        public Task<IReadOnlyList<DiagnosticItem>> Run(CancellationToken cancellationToken)
        {
            var (total, free) = _systemInfo.Memory();
            var disk = _systemInfo.FreeDiskBytes(_paths.Root);
            var items = new List<DiagnosticItem>
            {
                new(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.windows", ("version", Environment.OSVersion.Version.ToString()))),
                new(Area, total < 4096 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    new TextRef("diagnostics.ram", ("totalMb", total), ("freeMb", free)),
                    Solution: total < 4096 ? new TextRef("diagnostics.ram.solution") : null),
                new(Area, disk < 5L * 1024 * 1024 * 1024 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    new TextRef("diagnostics.disk", ("freeGb", disk / 1024 / 1024 / 1024)),
                    Solution: disk < 5L * 1024 * 1024 * 1024 ? new TextRef("diagnostics.disk.solution") : null)
            };
            foreach (var (name, version) in GraphicsCards())
            {
                items.Add(new DiagnosticItem(Area, DiagnosticStatus.Ok, new TextRef("diagnostics.gpu", ("name", name), ("driver", version))));
            }

            return Task.FromResult<IReadOnlyList<DiagnosticItem>>(items);
        }

        /// <summary>Grafikkarten und Treiberversion aus der Registry (ohne WMI).</summary>
        internal IEnumerable<(string Name, string Driver)> GraphicsCards()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (key == null)
            {
                yield break;
            }

            foreach (var sub in key.GetSubKeyNames().Where(subKeyName => subKeyName.All(char.IsDigit)))
            {
                using var card = key.OpenSubKey(sub);
                if (card?.GetValue("DriverDesc") is string name && card.GetValue("DriverVersion") is string version)
                {
                    yield return (_textHelper.Clean(name, 80), _textHelper.Clean(version, 40));
                }
            }
        }
    }
}
