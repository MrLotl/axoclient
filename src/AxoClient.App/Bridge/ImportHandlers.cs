using System.IO.Compression;
using AxoClient.Core.Common;
using AxoClient.Core.Import;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record ForeignImportData(List<ForeignImportChoice> Choices);

    private sealed record FileRefData(string FileRef);

    private sealed record TransferData(string SourceId, string TargetId, ForeignImportParts Parts);

    // Entpackte Exporte fremder Launcher: Obergrenzen gegen ZIP-Bomben
    private const long MaxExtractBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxExtractEntries = 200_000;

    private static void RegisterImport(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        // Andere Launcher an ihren Standard-Speicherorten durchsuchen
        r.On("import.scan", async _ =>
            await host.Jobs.RunAsync("import.scan", new TextRef("import.scanTitle"), job => host.Foreign.ScanAsync(job)));

        // Ausgewählte Instanzen übernehmen. Ergebnis pro Instanz (mit Hinweisen), die UI zeigt eine Zusammenfassung.
        r.On<ForeignImportData>("import.run", async (d, _) =>
        {
            if (d.Choices.Count == 0)
                throw new AxoException("errors.invalidInput");
            var results = await host.Jobs.RunAsync("import.run", TextRef.Of("import.runTitle", ("count", d.Choices.Count)),
                job => host.ForeignImport.ImportAsync(d.Choices, job));
            await host.Instances.ListAsync();
            return results;
        });

        // In eine bestehende Instanz übernehmen: andere AxoClient-Instanzen als zusätzliche Quelle
        r.On<IdData>("import.ownSources", async (d, ct) => await host.Transfer.OwnSourcesAsync(Text.RequireId(d.Id), ct));

        r.On<TransferData>("import.into", async (d, _) =>
        {
            var target = Text.RequireId(d.TargetId);
            if (host.Games.IsRunning(target))
                throw new AxoException("instances.running");
            var result = await host.Jobs.RunAsync("import.into", new TextRef("transfer.title"),
                job => host.Transfer.TransferAsync(d.SourceId, target, d.Parts, job), target);
            await host.Instances.ListAsync();
            return result;
        });

        // Ordner einer Instanz (z. B. aus einem portablen Launcher) wählen
        r.On("import.folder", async _ =>
        {
            var folder = await ctx.Window.OpenFolderAsync("Instanzordner wählen");
            if (folder == null)
                return null;
            return host.Foreign.FromFolder(folder, Path.GetFileName(folder.TrimEnd('\\', '/'))).ToView();
        });

        // Datei: .mrpack und AxoClient-Backups direkt; andere ZIPs (Prism/MultiMC-Export, CurseForge-Modpack) werden
        // entpackt und wie ein fremder Ordner gelesen – die UI zeigt sie dann zur Kontrolle wie eine gefundene Instanz.
        r.On<FileRefData>("import.file", async (d, _) =>
        {
            var path = ctx.Files.Resolve(d.FileRef);
            if (IsDirectImport(path))
            {
                var instance = await host.Jobs.RunAsync("instance.import", TextRef.Of("instances.importing", ("file", Path.GetFileName(path))),
                    job => host.Backups.ImportAsync(path, job));
                return new { kind = "instance", instance };
            }
            var folder = await host.Jobs.RunAsync("import.extract", TextRef.Of("import.extracting", ("file", Path.GetFileName(path))),
                job => ExtractAsync(path, Path.Combine(host.Paths.Cache, "import", Guid.NewGuid().ToString("N")[..10]), job));
            var foreign = host.Foreign.FromFolder(folder, Path.GetFileNameWithoutExtension(path));
            return new { kind = "foreign", foreign = foreign.ToView() };
        });
    }

    private static bool IsDirectImport(string path)
    {
        if (path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry("modrinth.index.json") != null || zip.GetEntry("axoclient-backup.json") != null;
        }
        catch (InvalidDataException)
        {
            throw new AxoException("import.unknownFormat");
        }
    }

    /// <summary>Entpackt sicher (nur innerhalb des Zielordners) und steigt in einen einzelnen Oberordner hinein.</summary>
    private static async Task<string> ExtractAsync(string archive, string target, JobContext job)
    {
        Directory.CreateDirectory(target);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > MaxExtractEntries || zip.Entries.Sum(e => e.Length) > MaxExtractBytes)
            throw new AxoException("import.tooLarge");
        var files = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
        for (var i = 0; i < files.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            var destination = PathGuard.Combine(target, files[i].FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var from = files[i].Open())
            await using (var to = File.Create(destination))
                await from.CopyToAsync(to, job.Token);
            job.Report((double)(i + 1) / files.Count);
        }
        var root = target;
        while (Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root) is [var only])
            root = only;
        return root;
    }
}
