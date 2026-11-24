using AxoClient.App;
using AxoClient.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AxoClient.Tests
{
    public class StartupTests
    {
        [Fact]
        public async Task AllServicesCanBeResolved()
        {
            var root = Path.Combine(Path.GetTempPath(), "axo-startup-" + Guid.NewGuid().ToString("N"));
            var services = new ServiceCollection();
            new Startup(new AppPaths(root)).ConfigureServices(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            foreach (var descriptor in services)
            {
                Assert.NotNull(provider.GetService(descriptor.ServiceType));
            }
        }
    }
}
