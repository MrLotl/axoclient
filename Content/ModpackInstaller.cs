using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace McLauncher;

public record ModpackResult(Installation Instance, List<string> Report);

/// <summary>
/// Installiert Modrinth-Modpacks (.mrpack) als neue Instanz. Ein Pack ist ein Zip mit einer Dateiliste
/// (modrinth.index.json: Pfad, Prüfsummen, Download-Adressen) und einem Ordner "overrides" mit fertigen Dateien
/// (Einstellungen usw.). Das Archiv stammt aus dem Netz und wird deshalb wie eine fremde Eingabe behandelt.
/// </summary>
public class ModpackInstaller(AppState app)
{
    // Grenzen gegen kaputte oder böswillige Packs
    private const long MaxIndexBytes = 8 * 1024 * 1024;
    private const int MaxFiles = 5000;
    private const int MaxOverrideEntries = 30_000;
    private const long MaxOverrideBytes = 2L * 1024 * 1024 * 1024;
    private const int Parallel = 8;
    private const int Attempts = 3;

    /// <summary>Interna des Launchers; ein Pack darf sie nicht mitbringen (z.B. eine gefälschte Herkunftsliste).</summary>
    private static readonly string[] ReservedPaths = ["launcher-content.json", "launcher-icons/"];

    private record PackFile(string Path, string? Sha1, string? Sha512, List<string> Urls, long Size, bool Optional);

    private record PackIndex(string Name, string Minecraft, LoaderType Loader, string? LoaderVersion, List<PackFile> Files);

    /// <summary>Lädt die .mrpack-Datei einer Modrinth-Version herunter und installiert sie.</summary>
    public async Task<ModpackResult> InstallFromVersionAsync(ContentVersion version, string title, string name, WorkProgress progress)
    {
        if (!Downloads.IsAllowedModpackUrl(version.DownloadUrl))
            throw new InvalidOperationException("Diese Modpack-Version hat keine Datei, die AxoClient laden darf.");

        var temp = Path.Combine(Path.GetTempPath(), $"axoclient-{Guid.NewGuid():N}.mrpack");
        try
        {
            progress.Text.Report($"Lade {title}...");
            long received = 0;
            await Downloads.DownloadToFileAsync(app.Http, version.DownloadUrl!, temp, bytes =>
            {
                var total = Interlocked.Add(ref received, bytes);
                if (version.Size > 0)
                    progress.Fraction.Report(Math.Min(1.0, (double)total / version.Size) * 0.15); // erste 15 %: Pack-Datei
            }, progress.Cancel);
            return await InstallFromFileAsync(temp, name, progress);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // liegt eben noch im Temp-Ordner
            }
        }
    }

    /// <summary>Installiert eine .mrpack-Datei von der Festplatte.</summary>
    public async Task<ModpackResult> InstallFromFileAsync(string mrpackPath, string? name, WorkProgress progress)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(mrpackPath);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("Die Datei ist kein gültiges Modpack (.mrpack).");
        }

        using (zip)
        {
            var pack = ReadIndex(zip);

            progress.Text.Report("Prüfe Minecraft-Version...");
            var versions = await VersionCatalog.GetVersionsAsync(app.Http, pack.Loader, snapshots: true, oldVersions: true);
            if (!versions.Contains(pack.Minecraft))
                throw new InvalidOperationException(
                    $"{pack.Loader} gibt es für Minecraft {pack.Minecraft} nicht (mehr) – dieses Modpack kann nicht installiert werden.");

            var inst = new Installation
            {
                Name = InstanceFactory.UniqueName(app.Settings, ShareValidation.CleanText(name ?? pack.Name, 60)),
                Loader = pack.Loader,
                MinecraftVersion = pack.Minecraft,
                LoaderVersion = pack.LoaderVersion
            };
            inst.GameDir = InstanceFactory.NewGameDir(inst.Name, inst.Id);

            var report = new List<string>();
            try
            {
                Directory.CreateDirectory(inst.GameDir);
                await DownloadFilesAsync(pack, inst, progress, report);
                var overrides = await Task.Run(() => ExtractOverrides(zip, inst.GameDir, progress));
                await RegisterOriginsAsync(inst, progress, report);
                progress.Cancel.ThrowIfCancellationRequested();

                report.Insert(0, $"Modpack \"{pack.Name}\" wurde als Instanz \"{inst.Name}\" ({inst.Description}) installiert.");
                report.Add($"{pack.Files.Count} Dateien geladen, {overrides} Dateien aus dem Pack übernommen.");
            }
            catch
            {
                InstanceFactory.DiscardDirectory(inst.GameDir); // Fehler oder Abbruch: keine halbe Instanz zurücklassen
                throw;
            }

            app.Settings.Installations.Add(inst);
            app.NotifyInstallationsChanged();

            if (inst.LoaderVersion != null)
                report.Add($"{inst.Loader} {inst.LoaderVersion} und Minecraft werden beim ersten Start automatisch geladen.");
            else
                report.Add($"{inst.Loader} und Minecraft werden beim ersten Start automatisch geladen.");
            return new ModpackResult(inst, report);
        }
    }

    // ================= Index lesen =================

    private static PackIndex ReadIndex(ZipArchive zip)
    {
        var entry = zip.GetEntry("modrinth.index.json")
                    ?? throw new InvalidOperationException("Das ist kein Modrinth-Modpack (modrinth.index.json fehlt).");
        if (entry.Length > MaxIndexBytes)
            throw new InvalidOperationException("Die Dateiliste des Modpacks ist ungewöhnlich groß.");

        // Auf die Größe der Kopfzeile ist kein Verlass: beim Lesen selbst begrenzen
        using var buffer = new MemoryStream();
        using (var stream = entry.Open())
        {
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxIndexBytes)
                    throw new InvalidOperationException("Die Dateiliste des Modpacks ist ungewöhnlich groß.");
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(buffer.ToArray());
            return ParseIndex(doc.RootElement);
        }
        catch (UserFacingException)
        {
            throw; // schon eine verständliche Meldung (z.B. "braucht NeoForge")
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // fehlende oder falsch aufgebaute Felder in der Dateiliste
            throw new InvalidOperationException("Die Dateiliste des Modpacks ist beschädigt.");
        }
    }

    private static PackIndex ParseIndex(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("formatVersion", out var format) || format.GetInt32() != 1)
            throw new UserFacingException("Dieses Modpack hat ein Format, das AxoClient nicht kennt.");
        if (root.TryGetProperty("game", out var game) && game.GetString() != "minecraft")
            throw new UserFacingException("Dieses Modpack ist nicht für Minecraft.");

        var name = root.TryGetProperty("name", out var n) ? ShareValidation.CleanText(n.GetString(), 60) : "";
        if (name.Length == 0)
            name = "Modpack";

        var dependencies = root.GetProperty("dependencies");
        var minecraft = dependencies.GetProperty("minecraft").GetString();
        if (!ShareValidation.IsVersion(minecraft))
            throw new UserFacingException("Die Minecraft-Version im Modpack ist ungültig.");

        string? Dependency(string key) => dependencies.TryGetProperty(key, out var v) ? v.GetString() : null;

        LoaderType loader;
        string? loaderVersion;
        if (Dependency("fabric-loader") is { } fabric)
            (loader, loaderVersion) = (LoaderType.Fabric, fabric);
        else if (Dependency("forge") is { } forge)
            (loader, loaderVersion) = (LoaderType.Forge, forge);
        else if (Dependency("neoforge") != null)
            throw new UserFacingException("Dieses Modpack braucht NeoForge. AxoClient kann derzeit nur Fabric und Forge starten.");
        else if (Dependency("quilt-loader") != null)
            throw new UserFacingException("Dieses Modpack braucht Quilt. AxoClient kann derzeit nur Fabric und Forge starten.");
        else
            (loader, loaderVersion) = (LoaderType.Vanilla, null);
        if (!GameInstaller.IsSafeLoaderVersion(loaderVersion))
            loaderVersion = null; // dann gilt die neueste

        var files = new List<PackFile>();
        if (root.TryGetProperty("files", out var fileArray))
        {
            if (fileArray.GetArrayLength() > MaxFiles)
                throw new UserFacingException($"Dieses Modpack enthält zu viele Dateien (mehr als {MaxFiles}).");
            foreach (var file in fileArray.EnumerateArray())
            {
                // Was der Nutzer im Spiel nicht braucht (z.B. reine Server-Mods), gar nicht erst laden
                if (file.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var client)
                    && client.GetString() == "unsupported")
                    continue;

                var hashes = file.GetProperty("hashes");
                files.Add(new PackFile(
                    file.GetProperty("path").GetString() ?? "",
                    hashes.TryGetProperty("sha1", out var sha1) ? sha1.GetString()?.ToLowerInvariant() : null,
                    hashes.TryGetProperty("sha512", out var sha512) ? sha512.GetString()?.ToLowerInvariant() : null,
                    file.GetProperty("downloads").EnumerateArray().Select(d => d.GetString() ?? "").ToList(),
                    file.TryGetProperty("fileSize", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                    env.ValueKind == JsonValueKind.Object && env.TryGetProperty("client", out var kind) && kind.GetString() == "optional"));
            }
        }
        return new PackIndex(name, minecraft!, loader, loaderVersion, files);
    }

    // ================= Dateien laden =================

    private async Task DownloadFilesAsync(PackIndex pack, Installation inst, WorkProgress progress, List<string> report)
    {
        // Pfade und Adressen aller Dateien prüfen, bevor auch nur eine geladen wird
        var planned = new List<(PackFile File, string Destination, string Url)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in pack.Files)
        {
            var destination = Downloads.SafeCombine(inst.GameDir, file.Path)
                              ?? throw new InvalidOperationException($"Ungültiger Pfad im Modpack: \"{file.Path}\".");
            if (IsReserved(file.Path))
                throw new InvalidOperationException($"Ungültiger Pfad im Modpack: \"{file.Path}\".");
            var url = file.Urls.FirstOrDefault(Downloads.IsAllowedModpackUrl)
                      ?? throw new InvalidOperationException(
                          $"Das Modpack will \"{Path.GetFileName(file.Path)}\" von einem nicht erlaubten Server laden.");
            if (file.Sha1 == null && file.Sha512 == null)
                throw new InvalidOperationException($"Für \"{Path.GetFileName(file.Path)}\" fehlt die Prüfsumme.");
            if (seen.Add(destination))
                planned.Add((file, destination, url));
        }
        if (planned.Count == 0)
            return;

        var totalBytes = planned.Sum(p => p.File.Size);
        long receivedBytes = 0;
        var done = 0;
        var optionalFailed = new List<string>();
        Exception? fatal = null;

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(progress.Cancel);
        using var gate = new SemaphoreSlim(Parallel);

        try
        {
            await Task.WhenAll(planned.Select(async item =>
            {
                await gate.WaitAsync(abort.Token);
                try
                {
                    await DownloadOneAsync(item.File, item.Destination, item.Url, abort.Token, bytes =>
                    {
                        var total = Interlocked.Add(ref receivedBytes, bytes);
                        if (totalBytes > 0)
                            progress.Fraction.Report(0.15 + Math.Min(1.0, (double)total / totalBytes) * 0.75);
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (item.File.Optional)
                    {
                        lock (optionalFailed)
                            optionalFailed.Add(Path.GetFileName(item.File.Path));
                    }
                    else
                    {
                        // Eine fehlende Pflichtdatei macht das Pack unbrauchbar: alles andere abbrechen
                        fatal ??= new InvalidOperationException($"\"{Path.GetFileName(item.File.Path)}\" konnte nicht geladen werden: {ex.Message}");
                        abort.Cancel();
                    }
                }
                finally
                {
                    gate.Release();
                    progress.Text.Report($"Lade Dateien... {Interlocked.Increment(ref done)} von {planned.Count}");
                }
            }));
        }
        catch (OperationCanceledException) when (fatal != null)
        {
            // vom eigenen Abbruch nach dem ersten harten Fehler ausgelöst
        }

        if (fatal != null)
            throw fatal;
        progress.Cancel.ThrowIfCancellationRequested();
        if (optionalFailed.Count > 0)
            report.Add("Optionale Dateien konnten nicht geladen werden: " + string.Join(", ", optionalFailed) + ".");
    }

    /// <summary>Lädt eine Datei und vergleicht sie mit der Prüfsumme aus dem Pack; bei Abweichung wird neu versucht.</summary>
    private async Task DownloadOneAsync(PackFile file, string destination, string url, CancellationToken cancel,
        Action<long> bytesReceived)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                var (sha1, sha512) = await Downloads.DownloadToFileAsync(app.Http, url, destination, bytesReceived, cancel);
                var matches = file.Sha512 != null ? sha512 == file.Sha512 : sha1 == file.Sha1;
                if (matches)
                    return;
                File.Delete(destination);
                last = new InvalidDataException("Die Prüfsumme stimmt nicht mit dem Modpack überein.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                last = ex;
            }
        }
        throw last ?? new InvalidOperationException("Unbekannter Fehler.");
    }

    // ================= Overrides =================

    private static bool IsReserved(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return ReservedPaths.Any(r => normalized.Equals(r.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                                      || normalized.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Entpackt "overrides" und danach "client-overrides" (die gewinnen). Jeder Pfad wird geprüft, damit nichts
    /// außerhalb der Instanz landen kann, und Anzahl wie Gesamtgröße sind begrenzt (Schutz vor Zip-Bomben).
    /// </summary>
    private static int ExtractOverrides(ZipArchive zip, string gameDir, WorkProgress progress)
    {
        var count = 0;
        long bytes = 0;
        foreach (var prefix in new[] { "overrides/", "client-overrides/" })
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    continue; // Ordner

                progress.Cancel.ThrowIfCancellationRequested();
                var relative = entry.FullName[prefix.Length..];
                if (IsReserved(relative))
                    continue; // Interna des Launchers nicht überschreiben lassen
                var destination = Downloads.SafeCombine(gameDir, relative)
                                  ?? throw new InvalidOperationException($"Ungültiger Pfad im Modpack: \"{entry.FullName}\".");
                if (++count > MaxOverrideEntries)
                    throw new InvalidOperationException("Das Modpack enthält ungewöhnlich viele Dateien.");

                progress.Text.Report($"Entpacke {relative}...");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = entry.Open();
                using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                var chunk = new byte[81920];
                int read;
                while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
                {
                    bytes += read;
                    if (bytes > MaxOverrideBytes)
                        throw new InvalidOperationException("Das Modpack ist entpackt ungewöhnlich groß.");
                    target.Write(chunk, 0, read);
                }
            }
        }
        return count;
    }

    // ================= Herkunft der Mods =================

    /// <summary>
    /// Ordnet die geladenen Dateien über ihren Hash Modrinth-Projekten zu, damit Updates, Versionswechsel und
    /// das Teilen der Instanz auch für Modpack-Inhalte funktionieren.
    /// </summary>
    private async Task RegisterOriginsAsync(Installation inst, WorkProgress progress, List<string> report)
    {
        progress.Text.Report("Ordne die Inhalte Modrinth-Projekten zu...");
        progress.Fraction.Report(0.95);
        var store = new ContentStore(inst, app.Http);
        var modrinth = new ModrinthProvider(app.Http);
        try
        {
            foreach (var type in new[] { ContentType.Mod, ContentType.ResourcePack, ContentType.Shader })
                await Task.Run(() => store.IdentifyUnknownAsync(type, modrinth)); // liest und hasht jede Datei: nicht im UI-Thread
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Nicht schlimm: das Pack läuft trotzdem, nur Updates und Teilen kennen die Herkunft dann nicht
            report.Add("Hinweis: Die Herkunft der Mods konnte nicht bei Modrinth nachgeschlagen werden.");
        }
    }
}

/// <summary>Ein Fehler mit einer Meldung, die der Nutzer direkt lesen soll (nicht als "beschädigt" verpackt).</summary>
internal sealed class UserFacingException(string message) : InvalidOperationException(message);
