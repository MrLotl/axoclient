using System.IO.Abstractions;
using AxoClient.App;
using AxoClient.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AxoClient.Tests
{
    /// <summary>
    /// Baut die Dienste für Tests über denselben Container wie der Launcher. Standard: eigener leerer Ordner im Temp-Verzeichnis;
    /// optional mit eigenem Dateisystem (MockFileSystem) und eigenem HTTP-Handler (kein Netz).
    /// </summary>
    public sealed class TestServices
    {
        private readonly ServiceProvider _provider;

        public TestServices(string? root = null, IFileSystem? fs = null, HttpMessageHandler? http = null)
        {
            Paths = new AppPaths(root ?? Path.Combine(Path.GetTempPath(), "axo-test-" + Guid.NewGuid().ToString("N")), fs);
            var services = new ServiceCollection();
            new Startup(Paths).ConfigureServices(services);
            if (http != null)
            {
                services.AddSingleton<IHttpHandlerFactory>(new FixedHttpHandlerFactory(http));
            }
            _provider = services.BuildServiceProvider();
        }

        public AppPaths Paths { get; }

        public T Get<T>() where T : notnull
        {
            return _provider.GetRequiredService<T>();
        }

        /// <summary>Neues Objekt mit eigenen Argumenten, den Rest liefert der Container.</summary>
        public T Create<T>(params object[] args)
        {
            return ActivatorUtilities.CreateInstance<T>(_provider, args);
        }

        private sealed class FixedHttpHandlerFactory : IHttpHandlerFactory
        {
            private readonly HttpMessageHandler _handler;

            public FixedHttpHandlerFactory(HttpMessageHandler handler)
            {
                _handler = handler;
            }

            public HttpMessageHandler Create()
            {
                return _handler;
            }
        }
    }
}
