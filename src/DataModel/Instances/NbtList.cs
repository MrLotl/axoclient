namespace AxoClient.DataModel.Instances
{
    public sealed class NbtList : List<object>
    {
        public NbtList(byte elementType)
        {
            ElementType = elementType;
        }

        public byte ElementType { get; set; }
    }
}
