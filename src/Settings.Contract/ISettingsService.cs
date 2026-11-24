using AxoClient.DataModel.Settings;

namespace AxoClient.Settings.Contract
{
    public interface ISettingsService
    {
        LauncherSettings Current { get; }

        UiState State { get; }

        event Action<LauncherSettings>? Changed;

        Task Load();

        Task Save(LauncherSettings settings);

        Task Update(Action<LauncherSettings> change);

        Task UpdateState(Action<UiState> change);
    }
}
