using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface IScreenshotManager
    {
        IReadOnlyList<ScreenshotInfo> List(string instanceId);

        string? PathOf(string instanceId, string file);

        void Rename(string instanceId, string file, string newName);

        void Delete(string instanceId, string file);
    }
}
