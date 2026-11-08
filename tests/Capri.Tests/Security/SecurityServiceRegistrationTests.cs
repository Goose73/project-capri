using System.IO;
using Capri.Core.Abstractions;
using Capri.Providers;
using Capri.Providers.DependencyInjection;
using Capri.Security;
using Capri.Security.DependencyInjection;
using Capri.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace Capri.Tests.Security;

public sealed class SecurityServiceRegistrationTests
{
    [Fact]
    public void SecurityAndProviderServicesResolve()
    {
        using var temp = new TempDirectory();
        var services = new ServiceCollection();

        services.AddCapriSecurity(new CapriPaths(Path.Combine(temp.Path, "capri")));
        services.AddCapriProviders();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<DpapiCredentialStore>(provider.GetRequiredService<ISecureCredentialStore>());
        Assert.IsType<JsonAppSettingsStore>(provider.GetRequiredService<IAppSettingsStore>());
        Assert.IsType<DefaultPermissionService>(provider.GetRequiredService<IPermissionService>());
        Assert.IsType<OpenAiCompatibleChatProvider>(provider.GetRequiredService<IChatProvider>());
        Assert.NotNull(provider.GetRequiredService<IModelRouter>());
    }
}
