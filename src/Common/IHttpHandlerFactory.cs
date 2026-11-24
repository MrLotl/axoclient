namespace AxoClient.Common
{
    public interface IHttpHandlerFactory
    {
        HttpMessageHandler Create();
    }
}
