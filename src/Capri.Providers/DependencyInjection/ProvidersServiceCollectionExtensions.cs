using Capri.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Capri.Providers.DependencyInjection;

/// <summary>
/// Registration helpers for chat provider services.
/// </summary>
public static class ProvidersServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAI-compatible provider and the Phase 1 model router.
    /// Add <c>AddCapriSecurity</c> first so settings and credentials resolve.
    /// </summary>
    public static IServiceCollection AddCapriProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Individual operations apply their own timeouts. An infinite client
        // timeout keeps long-lived streaming responses alive.
        services.AddHttpClient(OpenAiCompatibleChatProvider.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.TryAddSingleton<OpenAiCompatibleChatProvider>(serviceProvider =>
            new OpenAiCompatibleChatProvider(
                serviceProvider
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(OpenAiCompatibleChatProvider.HttpClientName),
                serviceProvider.GetRequiredService<IAppSettingsStore>(),
                serviceProvider.GetRequiredService<ISecureCredentialStore>(),
                serviceProvider.GetService<ILogger<OpenAiCompatibleChatProvider>>()));

        services.TryAddSingleton<IChatProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<OpenAiCompatibleChatProvider>());

        services.TryAddSingleton<IModelRouter, SingleProviderModelRouter>();

        return services;
    }
}
