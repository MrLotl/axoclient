using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Dialogs;

public static class ShareDialogs
{
    private const string FailureTitle = "Das hat nicht geklappt";

    private sealed record InstanceChoice(Installation Installation)
    {
        public override string ToString() => $"{Installation.Name}  ·  {Installation.Description}";
    }

    private sealed record OverlaySource(string? Profile)
    {
        public override string ToString() => Profile ?? "Aktuelle Einstellungen";
    }

    private sealed class FriendChooser
    {
        private readonly List<(FriendInfo Friend, CheckBox Box)> _boxes = [];
        private readonly TextBlock _problem = Ui.Problem();

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

        public FrameworkElement View { get; }

        public TextBlock Problem => _problem;

        public List<FriendInfo> Selected => _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Friend).ToList();

        public bool Validate() => Ui.ShowProblem(_problem, Selected.Count == 0 ? "Wähle mindestens einen Freund aus." : null);
    }

    public static Task ShareInstanceAsync(AppServices app, Installation? inst = null, string? preselectFriend = null,
        string? overlayProfile = null) =>
        UiRun.GuardAsync(app, FailureTitle, () => ShareInstanceCoreAsync(app, inst, preselectFriend, overlayProfile));

    public static Task ShareContentAsync(AppServices app, ContentPayload payload) =>
        UiRun.GuardAsync(app, FailureTitle, () => SharePayloadAsync(app, payload, $"{payload.KindText} teilen",
            $"\"{payload.Title}\" ({payload.KindText}) wird als Empfehlung geschickt. Dein Freund kann es mit einem " +
            "Klick installieren, und zwar in einer Instanz seiner Wahl.",
            ShareKinds.Content, payload.Title));

    public static Task ShareServerAsync(AppServices app, string name, string address)
    {
        var payload = new ServerPayload { Name = name, Address = address };
        return UiRun.GuardAsync(app, FailureTitle, () => SharePayloadAsync(app, payload, "Server teilen",
            $"\"{payload.Name}\" ({payload.Address}) wird an deine Freunde geschickt. Sie können den Server in die " +
            "Serverliste einer Instanz ihrer Wahl übernehmen.",
            ShareKinds.Server, payload.Name));
    }

    public static Task<bool> OpenShareAsync(AppServices app, ShareInfo share) =>
        UiRun.GuardAsync(app, FailureTitle, () => OpenShareCoreAsync(app, share));

    public static Task ImportPathAsync(AppServices app, string path) =>
        UiRun.GuardAsync(app, FailureTitle, () => OpenPathAsync(app, path));

    private static async Task<(List<FriendInfo> Friends, string? Problem)> LoadFriendsAsync(AppServices app)
    {
        if (app.Accounts.Session == null)
            return ([], "Melde dich an, um Freunden etwas zu schicken.");
        if (!app.Axo.Available)
            return ([], "Schalte unter Einstellungen \"Axolotl-Symbol in der Tabliste\" ein, um Freunden etwas zu schicken.");
        try
        {
            var friends = await app.Axo.GetMutualFriendsAsync();
            return (friends, friends.Count == 0
                ? "Du hast noch keine Freunde, die dich ebenfalls hinzugefügt haben. Nur mit ihnen kannst du etwas teilen."
                : null);
        }
        catch (Exception ex)
        {
            return ([], "Die Freunde konnten nicht geladen werden: " + ErrorReport.Short(ex));
        }
    }

    private static async Task ShareInstanceCoreAsync(AppServices app, Installation? inst, string? preselectFriend,
        string? overlayProfile)
    {
        var choices = app.Instances.All.Select(i => new InstanceChoice(i)).ToList();
        if (choices.Count == 0)
            return;
        var (friends, friendsProblem) = await LoadFriendsAsync(app);

        var instanceBox = Ui.Combo(choices);
        instanceBox.SelectedItem = choices.FirstOrDefault(c => c.Installation == (inst ?? app.Instances.Selected)) ?? choices[0];

        var whatGroup = Guid.NewGuid().ToString("N");
        var whole = Ui.Choice("Ganze Instanz", whatGroup, overlayProfile == null);
        var overlayOnly = Ui.Choice("Nur das Overlay", whatGroup, overlayProfile != null);

        var overlaySource = Ui.Combo([]);
        var overlayPanel = Ui.Stack(Ui.Label("Welches Overlay?"), overlaySource);

        var mods = Ui.Check("Mods");
        var packs = Ui.Check("Ressourcenpakete");
        var shaders = Ui.Check("Shader");
        var servers = Ui.Check("Server");
        var options = Ui.Check("Einstellungen & Tastenbelegung");
        var overlay = Ui.Check("Overlay-Einstellungen");
        var itemBoxes = new[] { mods, packs, shaders, servers, options, overlay };
        var items = Ui.Stack(itemBoxes.Cast<UIElement>()
            .Append(Ui.Note("Mods, Ressourcenpakete und Shader lädt dein Freund selbst von Modrinth. Nur was von " +
                            "Modrinth stammt, lässt sich teilen. Welten und das Instanzbild werden nicht übertragen.", 6, 11))
            .ToArray());

        var targetGroup = Guid.NewGuid().ToString("N");
        var toFriends = Ui.Choice("An Freunde senden", targetGroup, friends.Count > 0);
        var toFile = Ui.Choice("Als Datei speichern", targetGroup, friends.Count == 0);
        toFriends.IsEnabled = friends.Count > 0;

        var chooser = new FriendChooser(friends, preselectFriend);
        var friendPanel = Ui.Stack(chooser.View, friendsProblem != null ? Ui.Note(friendsProblem, 6, 11) : null);
        var fileNote = Ui.Note("Die Datei kann ein Freund im Launcher unter Instanzen → Importieren öffnen, " +
                               "auch wenn ihr nicht befreundet seid.", 6, 11);
        var problem = Ui.Problem();

        var form = Ui.Stack(
            inst == null ? Ui.Label("Instanz") : null, inst == null ? instanceBox : null,
            Ui.Label("Was möchtest du teilen?"), Ui.Row(whole, overlayOnly),
            overlayPanel, items,
            Ui.Label("An wen?"), Ui.Row(toFriends, toFile),
            friendPanel, fileNote, problem);

        Installation Current() => ((InstanceChoice)instanceBox.SelectedItem!).Installation;

        void RefreshItems()
        {
            var target = Current();
            var overlays = new OverlayStore(target);
            var sources = new List<object> { new OverlaySource(null) };
            sources.AddRange(overlays.ProfileNames().Select(n => new OverlaySource(n)));
            overlaySource.ItemsSource = sources;
            overlaySource.SelectedItem = sources.OfType<OverlaySource>().FirstOrDefault(s => s.Profile == overlayProfile) ?? sources[0];

            var store = app.ContentOf(target);
            var modCount = store.CountFiles(ContentType.Mod);
            var packCount = store.CountFiles(ContentType.ResourcePack);
            var shaderCount = store.CountFiles(ContentType.Shader);
            var serverCount = new ServerStore(target.GameDir).Load().Count;
            var hasOptions = File.Exists(target.OptionsFile);
            var hasOverlay = overlays.ReadActive() != null;
            Ui.SetOption(mods, $"Mods ({modCount})", target.CanUseMods && modCount > 0);
            Ui.SetOption(packs, $"Ressourcenpakete ({packCount})", packCount > 0);
            Ui.SetOption(shaders, $"Shader ({shaderCount})", shaderCount > 0);
            Ui.SetOption(servers, $"Server ({serverCount})", serverCount > 0);
            Ui.SetOption(options, hasOptions ? "Einstellungen & Tastenbelegung" : "Einstellungen (noch keine gespeichert)", hasOptions);
            Ui.SetOption(overlay, hasOverlay ? "Overlay-Einstellungen" : "Overlay-Einstellungen (noch keine gespeichert)", hasOverlay);
        }

        void RefreshVisibility()
        {
            Ui.Show(items, whole.IsChecked == true);
            Ui.Show(overlayPanel, overlayOnly.IsChecked == true);
            Ui.Show(friendPanel, toFriends.IsChecked == true);
            Ui.Show(fileNote, toFile.IsChecked == true);
        }

        foreach (var radio in new[] { whole, overlayOnly, toFriends, toFile })
            radio.Checked += (_, _) => RefreshVisibility();
        instanceBox.SelectionChanged += (_, _) => RefreshItems();
        RefreshItems();
        RefreshVisibility();

        string? ChosenProfile() => (overlaySource.SelectedItem as OverlaySource)?.Profile;

        JsonElement? ChosenOverlay() => ChosenProfile() is { } profile
            ? new OverlayStore(Current()).ReadProfile(profile)
            : new OverlayStore(Current()).ReadActive();

        bool Validate() => Ui.ShowProblem(problem,
            toFriends.IsChecked == true && chooser.Selected.Count == 0 ? "Wähle mindestens einen Freund aus."
            : whole.IsChecked == true && !itemBoxes.Any(Ui.IsChosen) ? "Wähle aus, was geteilt werden soll."
            : overlayOnly.IsChecked == true && ChosenOverlay() == null
                ? "Für diese Instanz gibt es noch keine Overlay-Einstellungen. Öffne im Spiel mit der rechten " +
                  "Umschalttaste das Overlay-Menü und speichere sie dort."
                : null);

        if (!await app.Dialogs.ShowFormAsync("Teilen", form, "Teilen", Validate))
            return;

        var source = Current();
        var chosenProfile = ChosenProfile();
        var onlyOverlay = overlayOnly.IsChecked == true;
        var exportOptions = new ExportOptions(Ui.IsChosen(mods), Ui.IsChosen(packs), Ui.IsChosen(shaders),
            Ui.IsChosen(servers), Ui.IsChosen(options), Ui.IsChosen(overlay));
        var recipients = toFriends.IsChecked == true ? chooser.Selected : [];

        string? filePath = null;
        if (toFile.IsChecked == true)
        {
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Paket speichern",
                Filter = "AxoClient-Paket (*.json)|*.json",
                FileName = Sanitize.FileName(chosenProfile ?? source.Name, "Instanz") + (onlyOverlay ? "-overlay" : "") +
                           ".axoclient.json"
            };
            if (save.ShowDialog(Application.Current.MainWindow) != true)
                return;
            filePath = save.FileName;
        }

        var report = await UiRun.RunAsync(app, "Paket wird vorbereitet",
            progress => BuildAndDeliverAsync(app, source, onlyOverlay ? chosenProfile ?? "" : null, exportOptions,
                recipients, filePath, progress),
            "Teilen fehlgeschlagen");
        if (report != null)
            await UiRun.ShowReportAsync(app, "Geteilt", report);
    }

    private static async Task<List<string>> BuildAndDeliverAsync(AppServices app, Installation inst, string? overlayOnly,
        ExportOptions options, List<FriendInfo> recipients, string? filePath, WorkProgress progress)
    {
        var report = new List<string>();
        string kind, title, json;
        if (overlayOnly != null)
        {
            var overlays = new OverlayStore(inst);
            var config = (overlayOnly.Length == 0 ? overlays.ReadActive() : overlays.ReadProfile(overlayOnly))
                         ?? throw new InvalidOperationException("Diese Overlay-Einstellungen gibt es nicht (mehr).");
            var payload = new OverlayPayload { Name = inst.Name, ProfileName = overlayOnly, Config = config };
            payload.Validate();
            var label = overlayOnly.Length == 0 ? "Overlay" : $"Overlay-Profil {payload.ProfileName}";
            (kind, title, json) = (ShareKinds.Overlay, $"{label} aus {inst.Name}", ShareJson.Write(payload));
            report.Add($"{label} aus \"{inst.Name}\" ({OverlayProfile.CountEnabled(config)} Anzeigen eingeschaltet).");
        }
        else
        {
            var result = await new InstanceExporter(app).BuildAsync(inst, options, progress);
            (kind, title, json) = (ShareKinds.Instance, inst.Name, ShareJson.Write(result.Manifest));
            report.Add($"Instanz \"{inst.Name}\" ({inst.Description}). {result.Manifest.Describe()}");
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

    private static async Task SharePayloadAsync(AppServices app, SharePayload payload, string dialogTitle, string intro,
        string kind, string title)
    {
        string json;
        try
        {
            payload.Validate();
            json = ShareJson.Write(payload);
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowErrorAsync("Teilen nicht möglich", ex);
            return;
        }

        var (friends, problem) = await LoadFriendsAsync(app);
        if (friends.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync(dialogTitle, problem ?? "Es gibt niemanden, dem du etwas schicken kannst.");
            return;
        }

        var chooser = new FriendChooser(friends, null);
        var form = Ui.Stack(Ui.Note(intro), Ui.Label("An wen?"), chooser.View, chooser.Problem);
        if (!await app.Dialogs.ShowFormAsync(dialogTitle, form, "Senden", chooser.Validate))
            return;

        var recipients = chooser.Selected;
        var report = await UiRun.RunAsync(app, "Wird gesendet",
            progress => SendAsync(app, recipients, kind, title, json, progress), "Senden fehlgeschlagen");
        if (report != null)
            await UiRun.ShowReportAsync(app, "Gesendet", report);
    }

    private static async Task<List<string>> SendAsync(AppServices app, List<FriendInfo> recipients, string kind,
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
                await app.Axo.SendShareAsync(friend.Uuid, kind, title, json);
                sent.Add(friend.Name);
            }
            catch (Exception ex)
            {
                failed.Add($"{friend.Name}: {ErrorReport.Short(ex)}");
            }
        }
        if (sent.Count > 0)
            report.Add($"Gesendet an {string.Join(", ", sent)}. Es erscheint auf der Startseite unter \"Geteilt mit dir\" " +
                       "(nach spätestens 14 Tagen verfällt es, wenn es nicht abgeholt wird).");
        if (failed.Count > 0)
            report.Add("Nicht zugestellt:\n" + string.Join("\n", failed));
        return report;
    }

    private static async Task<bool> OpenShareCoreAsync(AppServices app, ShareInfo share)
    {
        var json = await UiRun.RunAsync(app, "Paket wird geholt",
            _ => app.Axo.GetSharePayloadAsync(share.Id), "Das Paket konnte nicht geöffnet werden");
        if (json == null || !await HandlePayloadAsync(app, share.Kind, json, $"von {share.FromName}"))
            return false;
        try
        {
            await app.Axo.DeleteShareAsync(share.Id);
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Geteiltes Paket aus dem Postfach entfernen", ex);
        }
        return true;
    }

    private static async Task OpenPathAsync(AppServices app, string path)
    {
        if (path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            await ModpackDialogs.InstallFileAsync(app, path);
            return;
        }

        string text;
        try
        {
            if (new FileInfo(path).Length > ShareValidation.MaxFileBytes)
                throw new ShareFormatException("Die Datei ist zu groß für ein AxoClient-Paket.");
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is ShareFormatException or IOException)
        {
            await app.Dialogs.ShowErrorAsync("Datei nicht lesbar", ex);
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

    private static async Task<bool> HandlePayloadAsync(AppServices app, string kind, string json, string source)
    {
        try
        {
            return kind switch
            {
                ShareKinds.Instance => await ReceiveInstanceAsync(app, ShareJson.Read<InstanceManifest>(json, kind), source),
                ShareKinds.Overlay => await ReceiveOverlayAsync(app, ShareJson.Read<OverlayPayload>(json, kind), source),
                ShareKinds.Content => await ReceiveContentAsync(app, ShareJson.Read<ContentPayload>(json, kind), source),
                ShareKinds.Server => await ReceiveServerAsync(app, ShareJson.Read<ServerPayload>(json, kind), source),
                _ => await UnknownAsync(app)
            };
        }
        catch (ShareFormatException ex)
        {
            await app.Dialogs.ShowErrorAsync("Paket ungültig", ex);
            return false;
        }
    }

    private static async Task<bool> UnknownAsync(AppServices app)
    {
        await app.Dialogs.ShowMessageAsync("Unbekanntes Paket",
            "Dieses Paket stammt vermutlich aus einer neueren AxoClient-Version. Bitte aktualisiere den Launcher.");
        return false;
    }

    private static async Task<bool> ReceiveInstanceAsync(AppServices app, InstanceManifest manifest, string source)
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
        var names = string.Join(", ", manifest.Content.Take(10).Select(c => c.Title));
        var servers = manifest.Servers.Count > 0 ? Ui.Check($"Server übernehmen ({manifest.Servers.Count})") : null;
        var options = manifest.Options != null ? Ui.Check("Einstellungen & Tastenbelegung übernehmen") : null;
        var overlay = manifest.Overlay != null ? Ui.Check("Overlay-Einstellungen übernehmen") : null;
        var form = Ui.Stack(
            Ui.Note($"Eine Instanz {source}. Sie wird neu angelegt, deine anderen Instanzen bleiben unberührt."),
            Ui.Label("Name der Instanz"), nameBox,
            Ui.Note($"{manifest.Loader} · Minecraft {manifest.Minecraft}\n{manifest.Describe()}", 6),
            names.Length > 0 ? Ui.Note("Enthält u. a.: " + names + (manifest.Content.Count > 10 ? ", ..." : ""), 10, 11) : null,
            servers, options, overlay,
            Ui.Note("Alle Mods, Ressourcenpakete und Shader lädt AxoClient von Modrinth.", 0, 11));

        if (!await app.Dialogs.ShowFormAsync("Instanz übernehmen", form, "Installieren", () => nameBox.Text.Trim().Length > 0))
            return false;

        var choices = new ImportChoices(nameBox.Text.Trim(), Ui.IsChosen(servers), Ui.IsChosen(options), Ui.IsChosen(overlay));
        var result = await UiRun.RunAsync(app, "Instanz wird eingerichtet",
            progress => importer.ImportAsync(manifest, choices, progress), "Import fehlgeschlagen");
        if (result == null)
            return false;

        await UiRun.ShowReportAsync(app, "Instanz übernommen", result.Report);
        return true;
    }

    private static async Task<bool> ReceiveOverlayAsync(AppServices app, OverlayPayload payload, string source)
    {
        var what = payload.ProfileName.Length > 0 ? $"Overlay-Profil \"{payload.ProfileName}\"" : "Overlay-Einstellungen";
        var target = await ChooseInstanceAsync(app, "Overlay übernehmen",
            $"{what} {source}" + (payload.Name.Length > 0 ? $" (aus \"{payload.Name}\")" : "") +
            $": {OverlayProfile.CountEnabled(payload.Config)} Anzeigen eingeschaltet.\n\nIn welche Instanz sollen sie übernommen werden?",
            _ => true, preferred: i => BadgeMod.IsActiveFor(i, app.Settings), confirmText: "Weiter");
        if (target == null)
            return false;

        var overlays = new OverlayStore(target);
        var existing = overlays.ProfileNames();
        var nameBox = Ui.Input(payload.ProfileName.Length > 0 ? payload.ProfileName
            : payload.Name.Length > 0 ? OverlayStore.CleanProfileName("Overlay " + payload.Name) : "Overlay");
        var asProfile = Ui.Check("Als Profil speichern");
        var activate = Ui.Check("Sofort aktivieren (ersetzt die aktuellen Einstellungen, Sicherung als .bak)", false);
        var note = Ui.Note("", 6, 11);
        var form = Ui.Stack(asProfile, Ui.Label("Name des Profils"), nameBox, activate, note);

        bool Validate()
        {
            var name = OverlayStore.CleanProfileName(nameBox.Text);
            nameBox.IsEnabled = asProfile.IsChecked == true;
            note.Text = asProfile.IsChecked == true && existing.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? $"Ein Profil \"{name}\" gibt es schon; es wird ersetzt."
                : "";
            return activate.IsChecked == true || (asProfile.IsChecked == true && name.Length > 0);
        }

        nameBox.TextChanged += (_, _) => Validate();
        asProfile.Click += (_, _) => Validate();
        activate.Click += (_, _) => Validate();
        Validate();
        if (!await app.Dialogs.ShowFormAsync("Overlay übernehmen", form, "Übernehmen", Validate))
            return false;

        var doActivate = activate.IsChecked == true;
        if (doActivate && app.Games.IsRunning(target))
        {
            await app.Dialogs.ShowMessageAsync("Minecraft läuft noch",
                $"\"{target.Name}\" läuft gerade und würde die Einstellungen beim Schließen wieder überschreiben. " +
                "Beende das Spiel und öffne das Paket danach erneut, oder speichere es nur als Profil.");
            return false;
        }

        var received = OverlayProfile.From(payload.Config).Detached();
        string? savedAs = null;
        try
        {
            if (asProfile.IsChecked == true)
                savedAs = overlays.WriteProfile(nameBox.Text, received);
            if (doActivate)
                overlays.WriteActive(received.ToElement());
        }
        catch (IOException ex)
        {
            await app.Dialogs.ShowErrorAsync("Übernehmen fehlgeschlagen", ex);
            return false;
        }

        var lines = new List<string>();
        if (savedAs != null)
            lines.Add($"Profil \"{savedAs}\" gespeichert. Im Spiel (rechte Umschalttaste → Profile) lässt es sich laden.");
        if (doActivate)
            lines.Add($"Die Einstellungen gelten jetzt in \"{target.Name}\".");
        if (!BadgeMod.IsActiveFor(target, app.Settings))
            lines.Add("Achtung: Das Overlay gibt es nur mit Fabric und einer Minecraft-Version, für die AxoClient die Mod " +
                      "mitbringt. In dieser Instanz bleibt es zunächst ohne Wirkung.");
        await UiRun.ShowReportAsync(app, "Overlay übernommen", lines);
        return true;
    }

    private static async Task<bool> ReceiveContentAsync(AppServices app, ContentPayload payload, string source)
    {
        if (payload.InstallType is not { } type)
            return await ModpackDialogs.InstallProjectAsync(app, payload.ProjectId, payload.Title, payload.VersionId);

        var project = await UiRun.RunAsync(app, "Inhalt wird gesucht", async progress =>
        {
            progress.Text.Report("Frage Modrinth...");
            var (id, title) = await app.Modrinth.GetProjectInfoAsync(payload.ProjectId);
            return new ProjectSummary(id, title, null);
        }, "Auf Modrinth nicht gefunden");
        if (project == null)
            return false;

        var target = await ChooseInstanceAsync(app, $"{payload.KindText} installieren",
            $"\"{project.Title}\" ({payload.KindText}) {source}.\n\nIn welche Instanz soll es installiert werden?",
            i => type != ContentType.Mod || i.CanUseMods, preferred: null, confirmText: "Installieren");
        if (target == null)
            return false;

        var store = app.ContentOf(target);
        if (store.IsInstalled(project.Id))
        {
            await app.Dialogs.ShowMessageAsync("Schon installiert", $"\"{project.Title}\" ist in \"{target.Name}\" schon vorhanden.");
            return true;
        }

        var installed = await UiRun.RunAsync(app, "Wird installiert", async progress =>
        {
            var version = payload.VersionId != null
                          && await app.Modrinth.GetVersionAsync(payload.VersionId) is { } pinned
                          && pinned.ProjectId == project.Id && pinned.Supports(target, type)
                ? pinned
                : null;
            await store.InstallAsync(project.Id, project.Title, type, progress.Text, version: version);
            return project.Title;
        }, "Installation fehlgeschlagen");
        if (installed == null)
            return false;

        await app.Dialogs.ShowMessageAsync("Installiert", $"\"{installed}\" wurde in \"{target.Name}\" installiert.");
        return true;
    }

    private static async Task<bool> ReceiveServerAsync(AppServices app, ServerPayload payload, string source)
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
            await app.Dialogs.ShowErrorAsync("Übernehmen fehlgeschlagen", ex);
            return false;
        }
    }

    private static async Task<Installation?> ChooseInstanceAsync(AppServices app, string title, string intro,
        Func<Installation, bool> filter, Func<Installation, bool>? preferred, string confirmText)
    {
        var eligible = app.Instances.All.Where(filter).Select(i => new InstanceChoice(i)).ToList();
        if (eligible.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync(title,
                "Es gibt keine passende Instanz dafür (z.B. sind Mods in Vanilla-Instanzen nicht möglich). " +
                "Lege zuerst eine Instanz mit Fabric oder Forge an.");
            return null;
        }

        var box = Ui.Combo(eligible);
        box.SelectedItem = (preferred == null ? null : eligible.FirstOrDefault(c => preferred(c.Installation)))
                           ?? eligible.FirstOrDefault(c => c.Installation == app.Instances.Selected)
                           ?? eligible[0];
        var form = Ui.Stack(Ui.Note(intro), Ui.Label("Instanz"), box);
        return await app.Dialogs.ShowFormAsync(title, form, confirmText, () => box.SelectedItem != null)
            ? ((InstanceChoice)box.SelectedItem!).Installation
            : null;
    }
}
