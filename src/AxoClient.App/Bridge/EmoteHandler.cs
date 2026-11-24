using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Social;
using AxoClient.Integrations.Contract;
using AxoClient.Social.Contract;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Emote.</summary>
    public class EmoteHandler : BridgeHandler
    {
        private readonly IEmoteLibrary _emotes;
        private readonly IIngameLink _ingame;
        private readonly FileRefs _files;

        public EmoteHandler(IEmoteLibrary emotes, IIngameLink ingame, FileRefs files, IAppLog appLog, ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _emotes = emotes;
            _ingame = ingame;
            _files = files;
        }

        public override void Register(MessageRouter r, MainWindow window)
        {
            object Builtin() => _emotes.BuiltIn.Select(b => new { id = b.Id, name = b.Name, source = "axoclient" }).ToList();

            r.On("emotes.list", async ct => new
            {
                builtin = Builtin(),
                imported = await _emotes.List(ct),
                slots = await _emotes.Slots(ct)
            });
            r.On<EmoteImportData>("emotes.import", async (d, ct) =>
            {
                var added = new List<EmoteView>();
                foreach (var fileRef in d.FileRefs.Take(20))
                {
                    added.AddRange(await _emotes.Import(_files.Resolve(fileRef), ct));
                }

                return added;
            });
            r.On<EmoteRenameData>("emotes.rename", async (d, ct) =>
            {
                await _emotes.Rename(d.Id, d.Name, ct);
                return await _emotes.List(ct);
            });
            r.On<EmoteOptionsData>("emotes.setOptions", async (d, ct) =>
            {
                await _emotes.SetOptions(d.Id, d.Loop, d.Moving, ct);
                return await _emotes.List(ct);
            });
            r.On<IdData>("emotes.remove", async (d, ct) =>
            {
                await _emotes.Remove(d.Id, ct);
                return await _emotes.List(ct);
            });
            // Für die Vorschaubilder: Animation eines Emotes (eingebaut oder eigen) als JSON samt Name der Animation darin
            r.On<IdData>("emotes.animation", async (d, ct) =>
            {
                if (_emotes.BuiltIn.Any(b => b.Id == d.Id))
                {
                    return _emotes.BuiltInAnimations() is { } json ? new { json, animation = (string?)d.Id } : null;
                }

                var own = await _emotes.Animation(d.Id, ct);
                return own is { } a ? new { json = a.Json, animation = a.Animation } : null;
            });
            r.On<EmoteSlotsData>("emotes.setSlots", async (d, ct) =>
            {
                await _emotes.SetSlots(d.Slots, ct);
                return await _emotes.Slots(ct);
            });

            // Aus dem Axo-Menü: fehlende Mods für die offene Welt eines Freundes installieren und neu starten
            r.On<InstallMissingData>("ingame.installMissing", (d, _) => Task.FromResult<object?>(StartJob(
                _ingame.InstallMissing(d.InstanceId, d.Address, d.Mods.Select(m => (m.Source, m.ProjectId, m.VersionId)).ToList()))));
        }
    }
}
