namespace AxoClient.DataModel.Settings
{
    public sealed class GameSettings
    {
        public WindowMode WindowMode { get; set; } = WindowMode.Window;
        public Size2 WindowSize { get; set; } = new(1280, 720);
        public bool CheckModsOnLaunch { get; set; } = true;
    }
}
