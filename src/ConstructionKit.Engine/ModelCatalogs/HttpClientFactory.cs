namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class HttpClientFactory : IHttpClientFactory
{
    public IHttpClientWrapper CreateClient(Uri baseAddress)
    {
        return new HttpClientWrapper(baseAddress);
    }
}