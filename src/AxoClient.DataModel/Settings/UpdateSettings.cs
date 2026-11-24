namespace AxoClient.DataModel.Settings
{
    public sealed class UpdateSettings
    {
        public bool Auto { get; set; }
        public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    }
}
