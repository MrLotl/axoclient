namespace AxoClient.DataModel.Instances
{
    public sealed class NbtCompound : Dictionary<string, object>
    {
        public T? Get<T>(string key) => TryGetValue(key, out var v) && v is T t ? t : default;
    }
}
