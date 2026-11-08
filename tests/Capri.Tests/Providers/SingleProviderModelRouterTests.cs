using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Providers;
using Capri.Security;
using Capri.Tests.TestDoubles;
using NSubstitute;

namespace Capri.Tests.Providers;

public sealed class SingleProviderModelRouterTests
{
    [Fact]
    public async Task ResolvesConfiguredProviderAndModel()
    {
        var provider = Substitute.For<IChatProvider>();
        provider.ProviderId.Returns("openai");
        var router = new SingleProviderModelRouter(provider, new FakeAppSettingsStore());

        var route = await router.ResolveAsync(new ModelRouteRequest());

        Assert.Equal("openai", route.ProviderId);
        Assert.Equal(CapriDefaultSettings.DefaultModelId, route.ModelId);
        Assert.True(route.SupportsStreaming);
    }

    [Fact]
    public async Task ResolvesCustomProviderModelAndStreamingPreference()
    {
        var defaults = CapriDefaultSettings.Create();
        var settings = new FakeAppSettingsStore(defaults with
        {
            Provider = defaults.Provider with
            {
                ProviderId = "ollama",
                ChatModel = "llama3",
                EnableStreaming = false,
            },
        });

        var provider = Substitute.For<IChatProvider>();
        provider.ProviderId.Returns("ollama");
        var router = new SingleProviderModelRouter(provider, settings);

        var route = await router.ResolveAsync(new ModelRouteRequest());

        Assert.Equal("ollama", route.ProviderId);
        Assert.Equal("llama3", route.ModelId);
        Assert.False(route.SupportsStreaming);
    }

    [Fact]
    public async Task PreferredModelWinsOverSettings()
    {
        var provider = Substitute.For<IChatProvider>();
        provider.ProviderId.Returns("openai");
        var router = new SingleProviderModelRouter(provider, new FakeAppSettingsStore());

        var route = await router.ResolveAsync(
            new ModelRouteRequest(PreferredModelId: "gpt-4.1"));

        Assert.Equal("gpt-4.1", route.ModelId);
    }

    [Fact]
    public async Task EmptyConfiguredModelUsesFallback()
    {
        var defaults = CapriDefaultSettings.Create();
        var settings = new FakeAppSettingsStore(defaults with
        {
            Provider = defaults.Provider with { ChatModel = string.Empty },
        });

        var provider = Substitute.For<IChatProvider>();
        provider.ProviderId.Returns("openai");
        var router = new SingleProviderModelRouter(provider, settings);

        var route = await router.ResolveAsync(new ModelRouteRequest());

        Assert.Equal("gpt-4o-mini", route.ModelId);
    }

    [Fact]
    public async Task CancelledRequestThrows()
    {
        var provider = Substitute.For<IChatProvider>();
        var router = new SingleProviderModelRouter(provider, new FakeAppSettingsStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            router.ResolveAsync(new ModelRouteRequest(), cts.Token).AsTask());
    }
}
