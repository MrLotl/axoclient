using AxoClient.Common;
using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface IInstanceBackup
    {
        Task Export(string instanceId, string targetPath, JobContext job);

        /// <summary>Erkennt AxoClient-Backup oder .mrpack und liest beides als neue Instanz ein.</summary>
        Task<Instance> Import(string archivePath, JobContext job);
    }
}
