using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>
/// Alle Dialoge rund ums Teilen: eine Instanz, das Overlay, einen Mod oder einen Server an Freunde schicken (oder als
/// Datei speichern) – und das, was Freunde geschickt haben, prüfen und übernehmen.
/// Alles Empfangene ist fremde Eingabe: es wird erst geprüft (siehe <see cref="ShareJson"/>), dem Nutzer angezeigt und
/// nur nach seiner Bestätigung installiert. Dateien lädt der Launcher ausschließlich über Modrinth-Kennungen, nie über
/// Adressen aus einem Paket.
/// </summary>
internal static class ShareUi
{
    /// <summary>Eine Zeile in einer Instanzauswahl.</summary>
    private sealed record InstanceChoice(Installation Installation)
    {
        public override string ToString() => $"{Installation.Name}  ·  {Installation.Description}";
    }

    private sealed record ProjectName(string Id, string Title);

    // ================= Freunde =================

    /// <summary>Nur gegenseitige Freunde – nur an sie lässt der Dienst etwas zustellen.</summary>
    private static async Task<(List<FriendInfo> Friends, string? Problem)> LoadFriendsAsync(AppState app)
    {
        if (app.Session == null)
            return ([], "Melde dich an, um Freunden etwas zu schicken.");
        if (!app.Friends.Available)
            return ([], "Schalte unter Einstellungen \"Axolotl-Symbol in der Tabliste\" ein, um Freunden etwas zu schicken.");
        try
        {
            var friends = (await app.Friends.GetFriendsAsync()).Where(f => f.State == "friend").ToList();
            return (friends, friends.Count == 0
                ? "Du hast noch keine Freunde, die dich ebenfalls hinzugefügt haben. Nur mit ihnen kannst du etwas teilen."
                : null);
        }
        catch (Exception ex)
        {
            return ([], "Die Freunde konnten nicht geladen werden: " + ex.Message);
        }
    }

    /// <summary>Ankreuzliste der Freunde.</summary>
    private sealed class FriendChooser
    {
        private readonly List<(FriendInfo Friend, CheckBox Box)> _boxes = [];

        public FrameworkElement View { get; }

        public FriendChooser(IReadOnlyList<FriendInfo> friends, string? preselectUuid)
        {
            var list = new StackPanel();
            foreach (var friend in friends)
            {
                var box = Ui.Check(friend.Name, friend.Uuid == preselectUuid);
                _boxes.Add((friend, box));
                list.Children.Add(box);
            }
            View = friends.Count > 6 ? Ui.Scroll(list, 140) : list;
        }

        public List<FriendInfo> Selected => _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Friend).ToList();
    }

    // ================= Öffentliche Einstiegspunkte =================
    // Alles hier läuft aus "async void"-Klick-Handlern. Eine unbehandelte Ausnahme würde dort den ganzen Launcher
    // beenden – gerade bei fremden Paketen soll stattdessen eine verständliche Meldung erscheinen.

    private static async Task GuardAsync(AppState app, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // vom Nutzer abgebrochen
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowMessageAsync("Das hat nicht geklappt", ex.Message);
        }
    }

    private static async Task<bool> GuardAsync(AppState app, Func<Task<bool>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowMessageAsync("Das hat nicht geklappt", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Teilen-Dialog für eine Instanz. Ohne <paramref name="inst"/> lässt sich die Instanz im Dialog wählen
    /// (z.B. wenn er von der Freundesliste aus geöffnet wird).
    /// </summary>
    public static Task ShareInstanceAsync(AppState app, Installation? inst = null, string? preselectFriend = null) =>
        GuardAsync(app, () => ShareInstanceCoreAsync(app, inst, preselectFriend));

    /// <summary>Empfiehlt einen einzelnen Mod, ein Ressourcenpaket, einen Shader oder ein Modpack von Modrinth.</summary>
    public static Task ShareContentAsync(AppState app, ContentPayload payload, string? preselectFriend = null) =>
        GuardAsync(app, () => ShareContentCoreAsync(app, payload, preselectFriend));

    public static Task ShareServerAsync(AppState app, string name, string address) =>
        GuardAsync(app, () => ShareServerCoreAsync(app, name, address));

    /// <summary>
    /// Öffnet ein Paket aus dem Postfach. Nach erfolgreicher Übernahme verschwindet es aus dem Postfach;
    /// bricht der Nutzer ab, bleibt es liegen.
    /// </summary>
    public static Task<bool> OpenShareAsync(AppState app, ShareInfo share) =>
        GuardAsync(app, () => OpenShareCoreAsync(app, share));

    /// <summary>Öffnet eine Datei: ein AxoClient-Paket (.json) oder ein Modrinth-Modpack (.mrpack).</summary>
    public static Task ImportFileAsync(AppState app) =>
        GuardAsync(app, () => ImportFileCoreAsync(app));

    // ================= Senden: Instanz oder Overlay =================

    private static async Task ShareInstanceCoreAsync(AppState app, Installation? inst, string? preselectFriend)
    {
        var choices = app.Settings.Installations.Select(i => new InstanceChoice(i)).ToList();
        if (choices.Count == 0)
            return;
        var (friends, problem) = await LoadFriendsAsync(app);

        var form = new StackPanel();

        var instanceBox = Ui.Combo(choices);
        instanceBox.SelectedItem = choices.FirstOrDefault(c => c.Installation == (inst ?? app.SelectedInstallation)) ?? choices[0];
        if (inst == null)
        {
            form.Children.Add(Ui.Label("Instanz"));
            form.Children.Add(instanceBox);
        }

        // Was: ganze Instanz oder nur das Overlay
        var whatGroup = Guid.NewGuid().ToString("N");
        var whole = Ui.Choice("Ganze Instanz", whatGroup, true);
        var overlayOnly = Ui.Choice("Nur das Overlay", whatGroup);
        form.Children.Add(Ui.Label("Was möchtest du teilen?"));
        form.Children.Add(Ui.Row(whole, overlayOnly));

        var mods = Ui.Check("Mods");
        var packs = Ui.Check("Ressourcenpakete");
        var shaders = Ui.Check("Shader");
        var servers = Ui.Check("Server");
        var options = Ui.Check("Einstellungen & Tastenbelegung");
        var overlay = Ui.Check("Overlay-Einstellungen");
        var items = new StackPanel();
        foreach (var box in new[] { mods, packs, shaders, servers, options, overlay })
            items.Children.Add(box);
        items.Children.Add(Ui.Note("Mods, Ressourcenpakete und Shader lädt dein Freund selbst von Modrinth. Nur was von " +
                                   "Modrinth stammt, lässt sich teilen. Welten und das Instanzbild werden nicht übertragen.", 6, 11));
        form.Children.Add(items);

        // An wen: Freunde oder Datei
        var targetGroup = Guid.NewGuid().ToString("N");
        var toFriends = Ui.Choice("An Freunde senden", targetGroup, friends.Count > 0);
        var toFile = Ui.Choice("Als Datei speichern", targetGroup, friends.Count == 0);
        toFriends.IsEnabled = friends.Count > 0;
        form.Children.Add(Ui.Label("An wen?"));
        form.Children.Add(Ui.Row(toFriends, toFile));

        var chooser = new FriendChooser(friends, preselectFriend);
        var friendPanel = new StackPanel();
        friendPanel.Children.Add(chooser.View);
        if (problem != null)
            friendPanel.Children.Add(Ui.Note(problem, 6, 11));
        form.Children.Add(friendPanel);
        var fileNote = Ui.Note("Die Datei kann ein Freund im Launcher unter Instanzen → Importieren öffnen, " +
                               "auch wenn ihr nicht befreundet seid.", 6, 11);
        form.Children.Add(fileNote);

        var problemText = Ui.Problem();
        form.Children.Add(problemText);

        // ---- Zustand nachführen ----
        Installation Current() => ((InstanceChoice)instanceBox.SelectedItem!).Installation;

        void RefreshItems()
        {
            var target = Current();
            var store = new ContentStore(target, app.Http);
            var modCount = CountFiles(store, ContentType.Mod);
            var packCount = CountFiles(store, ContentType.ResourcePack);
            var shaderCount = CountFiles(store, ContentType.Shader);
            SetItem(mods, $"Mods ({modCount})", target.Loader != LoaderType.Vanilla && modCount > 0);
            SetItem(packs, $"Ressourcenpakete ({packCount})", packCount > 0);
            SetItem(shaders, $"Shader ({shaderCount})", shaderCount > 0);
            var serverCount = new ServerStore(target.GameDir).Load().Count;
            SetItem(servers, $"Server ({serverCount})", serverCount > 0);
            var hasOptions = File.Exists(Path.Combine(target.GameDir, "options.txt"));
            SetItem(options, hasOptions ? "Einstellungen & Tastenbelegung" : "Einstellungen (noch keine gespeichert)", hasOptions);
            var hasOverlay = OverlayConfigFile.Read(target) != null;
            SetItem(overlay, hasOverlay ? "Overlay-Einstellungen" : "Overlay-Einstellungen (noch keine gespeichert)", hasOverlay);
        }

        void RefreshVisibility()
        {
            items.Visibility = whole.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            friendPanel.Visibility = toFriends.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            fileNote.Visibility = toFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        whole.Checked += (_, _) => RefreshVisibility();
        overlayOnly.Checked += (_, _) => RefreshVisibility();
        toFriends.Checked += (_, _) => RefreshVisibility();
        toFile.Checked += (_, _) => RefreshVisibility();
        instanceBox.SelectionChanged += (_, _) => RefreshItems();
        RefreshItems();
        RefreshVisibility();

        bool AnyItem() => new[] { mods, packs, shaders, servers, options, overlay }.Any(b => b.IsEnabled && b.IsChecked == true);

        bool Validate()
        {
            string? reason = null;
            if (toFriends.IsChecked == true && chooser.Selected.Count == 0)
                reason = "Wähle mindestens einen Freund aus.";
            else if (whole.IsChecked == true && !AnyItem())
                reason = "Wähle aus, was geteilt werden soll.";
            else if (overlayOnly.IsChecked == true && OverlayConfigFile.Read(Current()) == null)
                reason = "Für diese Instanz gibt es noch keine Overlay-Einstellungen. Öffne im Spiel mit der rechten " +
                         "Umschalttaste das Overlay-Menü und speichere sie dort.";
            problemText.Text = reason ?? "";
            problemText.Visibility = reason == null ? Visibility.Collapsed : Visibility.Visible;
            return reason == null;
        }

        if (!await app.Dialogs.ShowFormAsync("Teilen", form, "Teilen", Validate))
            return;

        // ---- Auswerten ----
        var source = Current();
        var onlyOverlay = overlayOnly.IsChecked == true;
        var exportOptions = new ExportOptions(
            mods.IsEnabled && mods.IsChecked == true, packs.IsEnabled && packs.IsChecked == true,
            shaders.IsEnabled && shaders.IsChecked == true, servers.IsEnabled && servers.IsChecked == true,
            options.IsEnabled && options.IsChecked == true, overlay.IsEnabled && overlay.IsChecked == true);
        var recipients = toFriends.IsChecked == true ? chooser.Selected : new List<FriendInfo>();

        string? filePath = null;
        if (toFile.IsChecked == true)
        {
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Paket speichern",
                Filter = "AxoClient-Paket (*.json)|*.json",
                FileName = SafeFileName(source.Name) + (onlyOverlay ? "-overlay" : "") + ".axoclient.json"
            };
            if (save.ShowDialog(Application.Current.MainWindow) != true)
                return;
            filePath = save.FileName;
        }

        var report = await UiRun.RunAsync(app, "Paket wird vorbereitet",
            progress => BuildAndDeliverAsync(app, source, onlyOverlay, exportOptions, recipients, filePath, progress),
            "Teilen fehlgeschlagen");
        if (report != null)
            await UiRun.ShowReportAsync(app, "Geteilt", report);
    }

    private static async Task<List<string>> BuildAndDeliverAsync(AppState app, Installation inst, bool overlayOnly,
        ExportOptions options, List<FriendInfo> recipients, string? filePath, WorkProgress progress)
    {
        var report = new List<string>();
        string kind, title, json;
        if (overlayOnly)
        {
            var config = OverlayConfigFile.Read(inst)
                         ?? throw new InvalidOperationException("Für diese Instanz gibt es noch keine Overlay-Einstellungen.");
            var payload = new OverlayPayload { Name = inst.Name, Config = config };
            payload.Validate();
            (kind, title, json) = (ShareKinds.Overlay, $"Overlay aus {inst.Name}", ShareJson.Write(payload));
            report.Add($"Overlay-Einstellungen aus \"{inst.Name}\" ({OverlayConfigFile.CountEnabled(config)} Anzeigen eingeschaltet).");
        }
        else
        {
            var result = await new InstanceExporter(app).BuildAsync(inst, options, progress);
            (kind, title, json) = (ShareKinds.Instance, inst.Name, ShareJson.Write(result.Manifest));
            report.Add($"Instanz \"{inst.Name}\" ({inst.Description}). {Describe(result.Manifest)}");
            report.AddRange(result.Notes);
        }

        if (json.Length > ShareValidation.MaxJsonChars)
            throw new InvalidOperationException("Das Paket ist zu groß zum Teilen. Wähle weniger aus, z.B. ohne Einstellungen.");

        if (filePath != null)
        {
            await File.WriteAllTextAsync(filePath, json);
            report.Add($"Als Datei gespeichert:\n{filePath}");
        }
        report.AddRange(await SendAsync(app, recipients, kind, title, json, progress));
        return report;
    }

    // ================= Senden: einzelne Inhalte und Server =================

    private static async Task ShareContentCoreAsync(AppState app, ContentPayload payload, string? preselectFriend)
    {
        string json;
        try
        {
            payload.Validate();
            json = ShareJson.Write(payload);
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowMessageAsync("Teilen nicht möglich", ex.Message);
            return;
        }

        await ShareToFriendsAsync(app, $"{payload.KindText} teilen",
            $"\"{payload.Title}\" ({payload.KindText}) wird als Empfehlung geschickt. Dein Freund kann es mit einem " +
            "Klick installieren, und zwar in einer Instanz seiner Wahl.",
            ShareKinds.Content, payload.Title, json, preselectFriend);
    }

    private static async Task ShareServerCoreAsync(AppState app, string name, string address)
    {
        var payload = new ServerPayload { Name = name, Address = address };
        string json;
        try
        {
            payload.Validate();
            json = ShareJson.Write(payload);
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowMessageAsync("Teilen nicht möglich", ex.Message);
            return;
        }

        await ShareToFriendsAsync(app, "Server teilen",
            $"\"{payload.Name}\" ({payload.Address}) wird an deine Freunde geschickt. Sie können den Server in die " +
            "Serverliste einer Instanz ihrer Wahl übernehmen.",
            ShareKinds.Server, payload.Name, json, null);
    }

    /// <summary>Schlichter Dialog: Freunde ankreuzen, senden.</summary>
    private static async Task ShareToFriendsAsync(AppState app, string dialogTitle, string intro, string kind,
        string title, string json, string? preselectFriend)
    {
        var (friends, problem) = await LoadFriendsAsync(app);
        if (friends.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync(dialogTitle, problem ?? "Es gibt niemanden, dem du etwas schicken kannst.");
            return;
        }

        var chooser = new FriendChooser(friends, preselectFriend);
        var problemText = Ui.Problem();
        var form = new StackPanel();
        form.Children.Add(Ui.Note(intro));
        form.Children.Add(Ui.Label("An wen?"));
        form.Children.Add(chooser.View);
        form.Children.Add(problemText);

        if (!await app.Dialogs.ShowFormAsync(dialogTitle, form, "Senden", () =>
            {
                var ok = chooser.Selected.Count > 0;
                problemText.Text = ok ? "" : "Wähle mindestens einen Freund aus.";
                problemText.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
                return ok;
            }))
            return;

        var recipients = chooser.Selected;
        var report = await UiRun.RunAsync(app, "Wird gesendet",
            async progress => await SendAsync(app, recipients, kind, title, json, progress),
            "Senden fehlgeschlagen");
        if (report != null)
            await UiRun.ShowReportAsync(app, "Gesendet", report);
    }

    private static async Task<List<string>> SendAsync(AppState app, List<FriendInfo> recipients, string kind,
        string title, string json, WorkProgress progress)
    {
        var report = new List<string>();
        var sent = new List<string>();
        var failed = new List<string>();
        foreach (var friend in recipients)
        {
            progress.Cancel.ThrowIfCancellationRequested();
            progress.Text.Report($"Sende an {friend.Name}...");
            try
            {
                await app.Friends.SendShareAsync(friend.Uuid, kind, title, json);
                sent.Add(friend.Name);
            }
            catch (Exception ex)
            {
                failed.Add($"{friend.Name}: {ex.Message}");
            }
        }
        if (sent.Count > 0)
            report.Add($"Gesendet an {string.Join(", ", sent)}. Es erscheint auf der Startseite unter \"Geteilt mit dir\" " +
                       "(nach spätestens 14 Tagen verfällt es, wenn es nicht abgeholt wird).");
        if (failed.Count > 0)
            report.Add("Nicht zugestellt:\n" + string.Join("\n", failed));
        return report;
    }

    // ================= Empfangen =================

    private static async Task<bool> OpenShareCoreAsync(AppState app, ShareInfo share)
    {
        var json = await UiRun.RunAsync(app, "Paket wird geholt",
            _ => app.Friends.GetSharePayloadAsync(share.Id), "Das Paket konnte nicht geöffnet werden");
        if (json == null)
            return false;

        var handled = await HandlePayloadAsync(app, share.Kind, json, $"von {share.FromName}");
        if (handled)
        {
            try
            {
                await app.Friends.DeleteShareAsync(share.Id);
            }
            catch
            {
                // bleibt eben im Postfach, nächstes Mal ist es weg
            }
        }
        return handled;
    }

    private static async Task ImportFileCoreAsync(AppState app)
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Instanz, Overlay oder Modpack öffnen",
            Filter = "AxoClient-Paket oder Modrinth-Modpack|*.json;*.mrpack|Alle Dateien|*.*"
        };
        if (open.ShowDialog(Application.Current.MainWindow) != true)
            return;

        if (open.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            await ModpackUi.InstallFileAsync(app, open.FileName);
            return;
        }

        string text;
        try
        {
            if (new FileInfo(open.FileName).Length > 1_000_000)
                throw new ShareFormatException("Die Datei ist zu groß für ein AxoClient-Paket.");
            text = await File.ReadAllTextAsync(open.FileName);
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowMessageAsync("Datei nicht lesbar", ex.Message);
            return;
        }
        catch (IOException ex)
        {
            await app.Dialogs.ShowMessageAsync("Datei nicht lesbar", ex.Message);
            return;
        }

        if (ShareJson.DetectKind(text) is not { } kind)
        {
            await app.Dialogs.ShowMessageAsync("Keine AxoClient-Datei",
                "Diese Datei ist kein AxoClient-Paket. Modpacks von Modrinth haben die Endung .mrpack.");
            return;
        }
        await HandlePayloadAsync(app, kind, text, "aus einer Datei");
    }

    private static async Task<bool> HandlePayloadAsync(AppState app, string kind, string json, string source)
    {
        try
        {
            switch (kind)
            {
                case ShareKinds.Instance:
                    return await ReceiveInstanceAsync(app, ShareJson.Read<InstanceManifest>(json, kind), source);
                case ShareKinds.Overlay:
                    return await ReceiveOverlayAsync(app, ShareJson.Read<OverlayPayload>(json, kind), source);
                case ShareKinds.Content:
                    return await ReceiveContentAsync(app, ShareJson.Read<ContentPayload>(json, kind), source);
                case ShareKinds.Server:
                    return await ReceiveServerAsync(app, ShareJson.Read<ServerPayload>(json, kind), source);
                default:
                    await app.Dialogs.ShowMessageAsync("Unbekanntes Paket",
                        "Dieses Paket stammt vermutlich aus einer neueren AxoClient-Version. Bitte aktualisiere den Launcher.");
                    return false;
            }
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowMessageAsync("Paket ungültig", ex.Message);
            return false;
        }
    }

    // ---------- Instanz ----------

    private static async Task<bool> ReceiveInstanceAsync(AppState app, InstanceManifest manifest, string source)
    {
        var importer = new InstanceImporter(app);
        var checkedManifest = await UiRun.RunAsync(app, "Paket wird geprüft", async progress =>
        {
            progress.Text.Report("Frage Modrinth nach den Namen der Inhalte...");
            await importer.ResolveTitlesAsync(manifest);
            return manifest;
        }, "Das Paket konnte nicht geprüft werden");
        if (checkedManifest == null)
            return false;

        var nameBox = Ui.Input(manifest.Name.Length > 0 ? manifest.Name : "Importierte Instanz");
        var form = new StackPanel();
        form.Children.Add(Ui.Note($"Eine Instanz {source}. Sie wird neu angelegt, deine anderen Instanzen bleiben unberührt."));
        form.Children.Add(Ui.Label("Name der Instanz"));
        form.Children.Add(nameBox);
        form.Children.Add(Ui.Note($"{manifest.Loader} · Minecraft {manifest.Minecraft}\n{Describe(manifest)}", 6));

        var names = string.Join(", ", manifest.Content.Take(10).Select(c => c.Title));
        if (names.Length > 0)
            form.Children.Add(Ui.Note("Enthält u. a.: " + names + (manifest.Content.Count > 10 ? ", ..." : ""), 10, 11));

        var servers = manifest.Servers.Count > 0 ? Ui.Check($"Server übernehmen ({manifest.Servers.Count})") : null;
        var options = manifest.Options != null ? Ui.Check("Einstellungen & Tastenbelegung übernehmen") : null;
        var overlay = manifest.Overlay != null ? Ui.Check("Overlay-Einstellungen übernehmen") : null;
        foreach (var box in new[] { servers, options, overlay })
            if (box != null)
                form.Children.Add(box);
        form.Children.Add(Ui.Note("Alle Mods, Ressourcenpakete und Shader lädt AxoClient von Modrinth.", 0, 11));

        if (!await app.Dialogs.ShowFormAsync("Instanz übernehmen", form, "Installieren", () => nameBox.Text.Trim().Length > 0))
            return false;

        var choices = new ImportChoices(nameBox.Text.Trim(), servers?.IsChecked == true, options?.IsChecked == true,
            overlay?.IsChecked == true);
        var result = await UiRun.RunAsync(app, "Instanz wird eingerichtet",
            progress => importer.ImportAsync(manifest, choices, progress), "Import fehlgeschlagen");
        if (result == null)
            return false;

        await UiRun.ShowReportAsync(app, "Instanz übernommen", result.Report);
        return true;
    }

    // ---------- Overlay ----------

    private static async Task<bool> ReceiveOverlayAsync(AppState app, OverlayPayload payload, string source)
    {
        var count = OverlayConfigFile.CountEnabled(payload.Config);
        var target = await ChooseInstanceAsync(app, "Overlay übernehmen",
            $"Overlay-Einstellungen {source}" + (payload.Name.Length > 0 ? $" (aus \"{payload.Name}\")" : "") +
            $": {count} Anzeigen eingeschaltet.\n\nIn welche Instanz sollen sie übernommen werden? " +
            "Die bisherigen Einstellungen bleiben als Sicherung (.bak) erhalten.",
            _ => true, preferred: i => Badge.IsActiveFor(i, app.Settings), confirmText: "Übernehmen");
        if (target == null)
            return false;

        if (app.IsRunning(target))
        {
            await app.Dialogs.ShowMessageAsync("Minecraft läuft noch",
                $"\"{target.Name}\" läuft gerade und würde die Einstellungen beim Schließen wieder überschreiben. " +
                "Beende das Spiel und öffne das Paket danach erneut.");
            return false;
        }

        try
        {
            OverlayConfigFile.Write(target, payload.Config);
        }
        catch (IOException ex)
        {
            await app.Dialogs.ShowMessageAsync("Übernehmen fehlgeschlagen", ex.Message);
            return false;
        }

        await app.Dialogs.ShowMessageAsync("Overlay übernommen",
            $"Die Overlay-Einstellungen gelten jetzt in \"{target.Name}\"." +
            (Badge.IsActiveFor(target, app.Settings)
                ? ""
                : "\n\nAchtung: Das Overlay gibt es nur mit Fabric und einer Minecraft-Version, für die AxoClient die Mod " +
                  "mitbringt. In dieser Instanz bleibt es zunächst ohne Wirkung."));
        return true;
    }

    // ---------- Einzelner Inhalt ----------

    private static async Task<bool> ReceiveContentAsync(AppState app, ContentPayload payload, string source)
    {
        // Modpacks werden nicht in eine Instanz gelegt, sondern als neue angelegt
        if (payload.Kind == SharedContentKind.Modpack)
            return await ModpackUi.InstallProjectAsync(app, payload.ProjectId, payload.Title, payload.VersionId);

        var type = payload.InstallType!.Value;
        var modrinth = new ModrinthProvider(app.Http);
        var project = await UiRun.RunAsync(app, "Inhalt wird gesucht", async progress =>
        {
            progress.Text.Report("Frage Modrinth...");
            var (id, title) = await modrinth.GetProjectInfoAsync(payload.ProjectId); // echter Name, nicht der aus der Nachricht
            return new ProjectName(id, title);
        }, "Auf Modrinth nicht gefunden");
        if (project == null)
            return false;

        var target = await ChooseInstanceAsync(app, $"{payload.KindText} installieren",
            $"\"{project.Title}\" ({payload.KindText}) {source}.\n\nIn welche Instanz soll es installiert werden?",
            i => type != ContentType.Mod || i.Loader != LoaderType.Vanilla, preferred: null, confirmText: "Installieren");
        if (target == null)
            return false;

        var store = new ContentStore(target, app.Http);
        if (store.IsInstalled(ContentSource.Modrinth, project.Id))
        {
            await app.Dialogs.ShowMessageAsync("Schon installiert", $"\"{project.Title}\" ist in \"{target.Name}\" schon vorhanden.");
            return true;
        }

        var installed = await UiRun.RunAsync(app, "Wird installiert", async progress =>
        {
            // Die Version des Freundes, wenn sie zur Instanz passt, sonst die neueste passende
            ContentVersion? version = null;
            if (payload.VersionId != null
                && await modrinth.GetVersionAsync(project.Id, payload.VersionId) is { } pinned
                && pinned.ProjectId == project.Id && pinned.Supports(target, type))
                version = pinned;
            await store.InstallAsync(modrinth, project.Id, project.Title, type, progress.Text, version: version);
            return project.Title;
        }, "Installation fehlgeschlagen");
        if (installed == null)
            return false;

        await app.Dialogs.ShowMessageAsync("Installiert", $"\"{installed}\" wurde in \"{target.Name}\" installiert.");
        return true;
    }

    // ---------- Server ----------

    private static async Task<bool> ReceiveServerAsync(AppState app, ServerPayload payload, string source)
    {
        var target = await ChooseInstanceAsync(app, "Server übernehmen",
            $"Server \"{payload.Name}\" ({payload.Address}) {source}.\n\nIn die Serverliste welcher Instanz soll er aufgenommen werden?",
            _ => true, preferred: null, confirmText: "Übernehmen");
        if (target == null)
            return false;

        try
        {
            var added = new ServerStore(target.GameDir).Merge([ServerStore.Create(payload.Name, payload.Address)]);
            await app.Dialogs.ShowMessageAsync(added > 0 ? "Server übernommen" : "Schon vorhanden",
                added > 0
                    ? $"\"{payload.Name}\" steht jetzt in der Serverliste von \"{target.Name}\"."
                    : $"Ein Server mit dieser Adresse steht in \"{target.Name}\" schon in der Liste.");
            return true;
        }
        catch (IOException ex)
        {
            await app.Dialogs.ShowMessageAsync("Übernehmen fehlgeschlagen", ex.Message);
            return false;
        }
    }

    // ================= Hilfsmittel =================

    /// <summary>Lässt eine Instanz auswählen; null bei Abbruch (oder wenn keine passt).</summary>
    private static async Task<Installation?> ChooseInstanceAsync(AppState app, string title, string intro,
        Func<Installation, bool> filter, Func<Installation, bool>? preferred, string confirmText)
    {
        var eligible = app.Settings.Installations.Where(filter).Select(i => new InstanceChoice(i)).ToList();
        if (eligible.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync(title,
                "Es gibt keine passende Instanz dafür (z.B. sind Mods in Vanilla-Instanzen nicht möglich). " +
                "Lege zuerst eine Instanz mit Fabric oder Forge an.");
            return null;
        }

        var box = Ui.Combo(eligible);
        box.SelectedItem = (preferred == null ? null : eligible.FirstOrDefault(c => preferred(c.Installation)))
                           ?? eligible.FirstOrDefault(c => c.Installation == app.SelectedInstallation)
                           ?? eligible[0];
        var form = new StackPanel();
        form.Children.Add(Ui.Note(intro));
        form.Children.Add(Ui.Label("Instanz"));
        form.Children.Add(box);

        return await app.Dialogs.ShowFormAsync(title, form, confirmText, () => box.SelectedItem != null)
            ? ((InstanceChoice)box.SelectedItem!).Installation
            : null;
    }

    private static void SetItem(CheckBox box, string label, bool available)
    {
        box.Content = label;
        box.IsEnabled = available;
        box.IsChecked = available;
    }

    /// <summary>Wie viele Dateien im Ordner liegen (schnell, ohne Symbole zu laden).</summary>
    private static int CountFiles(ContentStore store, ContentType type)
    {
        var folder = store.FolderOf(type);
        if (!Directory.Exists(folder))
            return 0;
        return type == ContentType.Mod
            ? Directory.GetFiles(folder, "*.jar*").Count(f => !Path.GetFileName(f).StartsWith(Badge.ModFileName))
            : Directory.GetFiles(folder, "*.zip").Length + Directory.GetDirectories(folder).Length;
    }

    /// <summary>"Enthält: 12 Mods, 2 Ressourcenpakete, ..." – was in einem Paket steckt.</summary>
    private static string Describe(InstanceManifest manifest)
    {
        var parts = new List<string>();

        void Add(int count, string singular, string plural)
        {
            if (count > 0)
                parts.Add($"{count} {(count == 1 ? singular : plural)}");
        }

        Add(manifest.Content.Count(c => c.Type == ContentType.Mod), "Mod", "Mods");
        Add(manifest.Content.Count(c => c.Type == ContentType.ResourcePack), "Ressourcenpaket", "Ressourcenpakete");
        Add(manifest.Content.Count(c => c.Type == ContentType.Shader), "Shader", "Shader");
        Add(manifest.Servers.Count, "Server", "Server");
        if (manifest.Options != null)
            parts.Add("Einstellungen");
        if (manifest.Overlay != null)
            parts.Add("Overlay");
        return parts.Count == 0 ? "Enthält nur Version und Mod-Loader." : "Enthält: " + string.Join(", ", parts) + ".";
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return safe.Length == 0 ? "Instanz" : safe;
    }
}
