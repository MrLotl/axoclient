using AxoClient.Common;
using AxoClient.DataModel.Settings;
using AxoClient.Settings.Contract;

namespace AxoClient.Settings
{
    /// <summary>Hält launcher.json und state.json im Speicher und speichert Änderungen.</summary>
    public sealed class SettingsService : ISettingsService
    {
        private readonly IEventHub _events;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppearance _appearance;
        private readonly IJsonStoreFactory _jsonStoreFactory;
        private readonly JsonStore<LauncherSettings> _launcher;
        private readonly JsonStore<UiState> _state;

        public LauncherSettings Current { get; private set; } = new();
        public UiState State { get; private set; } = new();

        public event Action<LauncherSettings>? Changed;

        public SettingsService(
            AppPaths paths,
            IEventHub events,
            IJsonHelper jsonHelper,
            IAppearance appearance,
            IJsonStoreFactory jsonStoreFactory)
        {
            _events = events;
            _jsonHelper = jsonHelper;
            _appearance = appearance;
            _jsonStoreFactory = jsonStoreFactory;
            _launcher = _jsonStoreFactory.Create<LauncherSettings>(paths.Fs, paths.LauncherJson);
            _state = _jsonStoreFactory.Create<UiState>(paths.Fs, paths.StateJson);
        }

        public async Task Load()
        {
            Current = await _launcher.Load();
            validate(Current);
            State = await _state.Load();
        }

        public async Task Save(LauncherSettings settings)
        {
            validate(settings);
            await _launcher.Save(settings);
            Current = settings;
            Changed?.Invoke(settings);
            _events.Publish("settings.changed", settings);
        }

        public async Task Update(Action<LauncherSettings> change)
        {
            var copy = _jsonHelper.Deserialize<LauncherSettings>(_jsonHelper.Serialize(Current))!;
            change(copy);
            await Save(copy);
        }

        public async Task UpdateState(Action<UiState> change)
        {
            State = await _state.Update(change);
        }

        private void validate(LauncherSettings s)
        {
            s.Defaults.MemoryMb = Math.Clamp(s.Defaults.MemoryMb, 512, 65536);
            s.Game.WindowSize = new Size2(Math.Clamp(s.Game.WindowSize.W, 320, 7680), Math.Clamp(s.Game.WindowSize.H, 240, 4320));
            if (s.Defaults.Java != "auto" && !(s.Defaults.Java.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase)
                                              || s.Defaults.Java.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase)))
            {
                s.Defaults.Java = "auto";
            }

            _appearance.Validate(s.Appearance);
        }
    }
}
