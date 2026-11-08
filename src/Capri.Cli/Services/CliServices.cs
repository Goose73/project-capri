using Capri.Automation.Autonomy;
using Capri.Automation.DependencyInjection;
using Capri.Core.Abstractions;
using Capri.Core.Autonomy;
using Capri.Providers.DependencyInjection;
using Capri.Security;
using Capri.Security.Autonomy;
using Capri.Security.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Capri.Cli.Services;

/// <summary>
/// Default CLI service composition. Reuses <c>AddCapriSecurity</c>,
/// <c>AddCapriProviders</c>, and <c>AddCapriAutomation</c> so settings,
/// credentials, provider profiles, and the HTTP client factory behave exactly
/// as they do in the desktop app.
/// </summary>
public sealed class CliServices : ICliServices
{
    private readonly ServiceProvider _provider;

    private CliServices(ServiceProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public ICapriPaths Paths => _provider.GetRequiredService<ICapriPaths>();

    /// <inheritdoc />
    public IAppSettingsStore Settings => _provider.GetRequiredService<IAppSettingsStore>();

    /// <inheritdoc />
    public ISecureCredentialStore Credentials =>
        _provider.GetRequiredService<ISecureCredentialStore>();

    /// <inheritdoc />
    public IChatProvider? ChatProvider => _provider.GetService<IChatProvider>();

    /// <inheritdoc />
    public IModelRouter? ModelRouter => _provider.GetService<IModelRouter>();

    /// <inheritdoc />
    public IHttpClientFactory? HttpClientFactory => _provider.GetService<IHttpClientFactory>();

    /// <inheritdoc />
    public IOllamaClient? OllamaClient => _provider.GetService<IOllamaClient>();

    /// <inheritdoc />
    public ITaskControl? TaskControl => _provider.GetService<ITaskControl>();

    /// <inheritdoc />
    public ITaskHistoryStore? TaskHistory => _provider.GetService<ITaskHistoryStore>();

    /// <inheritdoc />
    public ITaskExecutor? TaskExecutor => _provider.GetService<ITaskExecutor>();

    /// <inheritdoc />
    public ITaskJournal? TaskJournal => _provider.GetService<ITaskJournal>();

    /// <summary>
    /// Builds the default service graph. No host is started and no network
    /// client is created until a command actually needs one.
    /// </summary>
    public static CliServices CreateDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCapriSecurity();
        services.AddCapriProviders();
        services.AddCapriAutomation();
        services.AddSingleton<ITaskControl, FileTaskControl>();
        services.AddSingleton<ITaskHistoryStore, FileTaskHistoryStore>();
        services.AddSingleton<ITaskExecutor, SafeTaskExecutor>();
        services.AddSingleton<ITaskJournal>(sp => new TaskMetadataJournal(
            sp.GetRequiredService<ICapriPaths>(),
            sp.GetService<ITaskHistoryStore>()));
        services.AddHttpClient("capri.ollama", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IOllamaClient>(serviceProvider =>
            new OllamaClient(serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient("capri.ollama")));

        return new CliServices(services.BuildServiceProvider());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }
}
