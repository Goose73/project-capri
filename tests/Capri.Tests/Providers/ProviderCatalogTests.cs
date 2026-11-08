using Capri.Core.Providers;

namespace Capri.Tests.Providers;

public sealed class ProviderCatalogTests
{
    private static readonly string[] KnownProviderIds =
        ["ollama", "lmstudio", "openrouter", "openai"];

    [Theory]
    [InlineData("ollama", "Ollama", "http://localhost:11434/v1", false, true, true)]
    [InlineData("lmstudio", "LM Studio", "http://127.0.0.1:1234/v1", false, true, true)]
    [InlineData("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", true, false, true)]
    [InlineData("openai", "OpenAI", "https://api.openai.com/v1", true, false, false)]
    public void KnownProfilesExposeStableMetadata(
        string id,
        string displayName,
        string defaultBaseUrl,
        bool requiresApiKey,
        bool isLocal,
        bool supportsFreeModelsOnly)
    {
        Assert.True(ProviderCatalog.TryGetById(id, out var profile));

        Assert.Equal(id, profile.Id);
        Assert.Equal(displayName, profile.DisplayName);
        Assert.Equal(defaultBaseUrl, profile.DefaultBaseUrl);
        Assert.Equal(requiresApiKey, profile.RequiresApiKey);
        Assert.Equal(isLocal, profile.IsLocal);
        Assert.Equal(!isLocal, profile.IsCloud);
        Assert.Equal(supportsFreeModelsOnly, profile.SupportsFreeModelsOnly);
        Assert.Equal(isLocal, profile.IsLoopbackAllowedHttp);
    }

    [Fact]
    public void CatalogContainsStableKnownProviderSet()
    {
        Assert.Equal(
            KnownProviderIds,
            ProviderCatalog.All.Select(profile => profile.Id));

        Assert.Equal(
            ProviderCatalog.All.Count,
            ProviderCatalog.All.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("OLLAMA", "ollama")]
    [InlineData("  OpenRouter  ", "openrouter")]
    public void TryGetByIdIsCaseInsensitiveAndTrims(
        string providerId,
        string expectedId)
    {
        Assert.True(ProviderCatalog.TryGetById(providerId, out var profile));
        Assert.Equal(expectedId, profile.Id);
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryGetByIdRejectsUnknownIdentifiers(string? providerId)
    {
        Assert.False(ProviderCatalog.TryGetById(providerId, out var profile));
        Assert.Null(profile);
    }

    [Fact]
    public void GetDefaultBaseUrlUsesCatalogAndReturnsEmptyForUnknown()
    {
        Assert.Equal("http://localhost:11434/v1", ProviderCatalog.GetDefaultBaseUrl("ollama"));
        Assert.Equal("http://127.0.0.1:1234/v1", ProviderCatalog.GetDefaultBaseUrl("lmstudio"));
        Assert.Equal("https://openrouter.ai/api/v1", ProviderCatalog.GetDefaultBaseUrl("openrouter"));
        Assert.Equal("https://api.openai.com/v1", ProviderCatalog.GetDefaultBaseUrl("openai"));
        Assert.Equal(string.Empty, ProviderCatalog.GetDefaultBaseUrl("unknown"));
        Assert.Equal(string.Empty, ProviderCatalog.GetDefaultBaseUrl(null));
    }

    [Fact]
    public void HelperMethodsReflectProviderAuthAndLocality()
    {
        Assert.True(ProviderCatalog.RequiresApiKey("openrouter"));
        Assert.True(ProviderCatalog.RequiresApiKey("openai"));
        Assert.False(ProviderCatalog.RequiresApiKey("ollama"));
        Assert.False(ProviderCatalog.RequiresApiKey("lmstudio"));
        Assert.False(ProviderCatalog.RequiresApiKey("unknown"));

        Assert.True(ProviderCatalog.IsLocal("ollama"));
        Assert.True(ProviderCatalog.IsLocal("lmstudio"));
        Assert.False(ProviderCatalog.IsLocal("openrouter"));
        Assert.False(ProviderCatalog.IsLocal("openai"));
        Assert.False(ProviderCatalog.IsLocal("unknown"));
    }

    [Fact]
    public void OpenRouterAndOpenAiAreCloudProviders()
    {
        Assert.True(ProviderCatalog.OpenRouter.IsCloud);
        Assert.False(ProviderCatalog.OpenRouter.IsLocal);
        Assert.True(ProviderCatalog.OpenRouter.RequiresApiKey);

        Assert.True(ProviderCatalog.OpenAi.IsCloud);
        Assert.False(ProviderCatalog.OpenAi.IsLocal);
        Assert.True(ProviderCatalog.OpenAi.RequiresApiKey);
    }

    [Fact]
    public void OpenRouterSupportsFreeModelsOnly()
    {
        Assert.True(ProviderCatalog.OpenRouter.SupportsFreeModelsOnly);
        Assert.False(ProviderCatalog.OpenAi.SupportsFreeModelsOnly);
        Assert.True(ProviderCatalog.Ollama.SupportsFreeModelsOnly);
        Assert.True(ProviderCatalog.LmStudio.SupportsFreeModelsOnly);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("http://[::1]:8080/v1", true)]
    [InlineData("https://localhost:11434/v1", false)]
    [InlineData("http://example.com/v1", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLoopbackAllowedHttpAcceptsOnlyPlainHttpLoopback(
        string? baseUrl,
        bool expected)
    {
        Assert.Equal(expected, ProviderCatalog.IsLoopbackAllowedHttp(baseUrl));
    }

    [Theory]
    [InlineData("http://localhost:11434/v1", "ollama")]
    [InlineData("http://127.0.0.1:1234/v1", "lmstudio")]
    [InlineData("https://openrouter.ai/api/v1", "openrouter")]
    [InlineData("https://api.openai.com/v1/", "openai")]
    public void TryGetByBaseUrlMatchesCatalogDefaults(
        string baseUrl,
        string expectedId)
    {
        Assert.True(ProviderCatalog.TryGetByBaseUrl(baseUrl, out var profile));
        Assert.Equal(expectedId, profile.Id);
    }
}
