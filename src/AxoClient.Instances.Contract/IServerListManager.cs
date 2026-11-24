using AxoClient.DataModel.Instances;

namespace AxoClient.Instances.Contract
{
    public interface IServerListManager
    {
        IReadOnlyList<ServerEntry> List(string instanceId);

        /// <summary>Normalisierte Adressen aller Server einer Instanz ("Freund spielt auf deinem Server").</summary>
        IReadOnlySet<string> NormalizedAddresses(string instanceId);

        /// <summary>Servericon als PNG (Base64 im NBT).</summary>
        byte[]? Icon(string instanceId, int index);

        Task Add(string instanceId, string name, string address, int? atIndex = null);

        Task Edit(string instanceId, int index, string name, string address);

        Task Remove(string instanceId, int index);

        Task Move(string instanceId, int from, int to);
    }
}
