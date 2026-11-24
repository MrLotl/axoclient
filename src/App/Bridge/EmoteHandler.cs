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

        public override void Register(MessageRouter router, MainWindow window)
        {
            object Builtin() => _emotes.BuiltIn.Select(builtInEmote => new { id = builtInEmote.Id, name = builtInEmote.Name, source = "axoclient" }).ToList();

            router.On("emotes.list", async cancellationToken => new
            {
                builtin = Builtin(),
                imported = await _emotes.List(cancellationToken),
                slots = await _emotes.Slots(cancellationToken)
            });
            router.On<EmoteImportData>("emotes.import", async (request, cancellationToken) =>
            {
                var added = new List<EmoteView>();
                foreach (var fileReference in request.FileRefs.Take(20))
                {
                    added.AddRange(await _emotes.Import(_files.Resolve(fileReference), cancellationToken));
                }

                return added;
            });
            router.On<EmoteRenameData>("emotes.rename", async (request, cancellationToken) =>
            {
                await _emotes.Rename(request.Id, request.Name, cancellationToken);
                return await _emotes.List(cancellationToken);
            });
            router.On<EmoteOptionsData>("emotes.setOptions", async (request, cancellationToken) =>
            {
                await _emotes.SetOptions(request.Id, request.Loop, request.Moving, cancellationToken);
                return await _emotes.List(cancellationToken);
            });
            router.On<IdData>("emotes.remove", async (request, cancellationToken) =>
            {
                await _emotes.Remove(request.Id, cancellationToken);
                return await _emotes.List(cancellationToken);
            });
            // Für die Vorschaubilder: Animation eines Emotes (eingebaut oder eigen) als JSON samt Name der Animation darin
            router.On<IdData>("emotes.animation", async (request, cancellationToken) =>
            {
                if (_emotes.BuiltIn.Any(builtInEmote => builtInEmote.Id == request.Id))
                {
                    return _emotes.BuiltInAnimations() is { } json ? new { json, animation = (string?)request.Id } : null;
                }

                var own = await _emotes.Animation(request.Id, cancellationToken);
                return own is { } ownAnimation ? new { json = ownAnimation.Json, animation = ownAnimation.Animation } : null;
            });
            router.On<EmoteSlotsData>("emotes.setSlots", async (request, cancellationToken) =>
            {
                await _emotes.SetSlots(request.Slots, cancellationToken);
                return await _emotes.Slots(cancellationToken);
            });

            // Aus dem Axo-Menü: fehlende Mods für die offene Welt eines Freundes installieren und neu starten
            router.On<InstallMissingData>("ingame.installMissing", (request, _) => Task.FromResult<object?>(StartJob(
                _ingame.InstallMissing(request.InstanceId, request.Address, request.Mods.Select(mod => (mod.Source, mod.ProjectId, mod.VersionId)).ToList()))));
        }
    }
}
