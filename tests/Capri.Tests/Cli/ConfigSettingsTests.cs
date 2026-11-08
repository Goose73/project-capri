using Capri.Cli.Configuration;
using Capri.Core.Models;
using Capri.Security;

namespace Capri.Tests.Cli;

public sealed class ConfigSettingsTests
{
    [Fact]
    public void DefaultValuesAreReadable()
    {
        var settings = CapriDefaultSettings.Create();

        Assert.Equal("openai", ConfigSettings.GetValue(settings, "provider.id"));
        Assert.Equal("gpt-4o-mini", ConfigSettings.GetValue(settings, "provider.model"));
        Assert.Equal(true, ConfigSettings.GetValue(settings, "provider.streaming"));
        Assert.Equal(true, ConfigSettings.GetValue(settings, "provider.freeOnly"));
        Assert.Equal(false, ConfigSettings.GetValue(settings, "provider.allowCloud"));
        Assert.Equal(250, ConfigSettings.GetValue(settings, "ui.characterHeight"));
    }

    [Theory]
    [InlineData("provider.apiKey")]
    [InlineData("provider.openai.apiKey")]
    [InlineData("provider.secret")]
    [InlineData("provider.token")]
    [InlineData("openai.credential")]
    [InlineData("automation.password")]
    public void SecretLikeNamesAreRejected(string key)
    {
        Assert.True(SecretGuard.IsSecretLike(key));
    }

    [Theory]
    [InlineData("provider.id")]
    [InlineData("provider.model")]
    [InlineData("provider.baseUrl")]
    [InlineData("ui.characterHeight")]
    [InlineData("provider.maxOutputTokens")]
    public void SafeNamesAreNotSecretLike(string key)
    {
        Assert.False(SecretGuard.IsSecretLike(key));
    }

    [Fact]
    public void UnknownKeysAreNotAllowlisted()
    {
        Assert.False(ConfigSettings.IsKnownKey("provider.apiKey"));
        Assert.False(ConfigSettings.IsKnownKey("unknown.setting"));
        Assert.Null(ConfigSettings.CanonicalizeKey("provider.apiKey"));
    }

    [Fact]
    public void SetModelPersistsValidatedValue()
    {
        var current = CapriDefaultSettings.Create();

        var success = ConfigSettings.TrySet(
            current,
            "provider.model",
            "llama3.2",
            out var updated,
            out var error);

        Assert.True(success);
        Assert.Empty(error);
        Assert.Equal("llama3.2", updated.Provider.ChatModel);
        Assert.Equal("gpt-4o-mini", current.Provider.ChatModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    public void InvalidModelValuesAreRejected(string value)
    {
        var success = ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "provider.model",
            value,
            out _,
            out var error);

        Assert.False(success);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("not-a-boolean")]
    [InlineData("2")]
    public void InvalidBooleansAreRejected(string value)
    {
        var success = ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "provider.streaming",
            value,
            out _,
            out _);

        Assert.False(success);
    }

    [Theory]
    [InlineData("0.1.2")]
    [InlineData("true")]
    public void InvalidCharacterHeightsAreRejected(string value)
    {
        var success = ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "ui.characterHeight",
            value,
            out _,
            out _);

        Assert.False(success);
    }

    [Fact]
    public void CharacterHeightBoundsAreEnforced()
    {
        Assert.True(ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "ui.characterHeight",
            "150",
            out _,
            out _));
        Assert.True(ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "ui.characterHeight",
            "400",
            out _,
            out _));
        Assert.False(ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "ui.characterHeight",
            "401",
            out _,
            out _));
    }

    [Theory]
    [InlineData("https://user:pass@example.com/v1")]
    [InlineData("https://example.com/v1?api_key=secret")]
    [InlineData("ftp://example.com/v1")]
    [InlineData("not a url")]
    public void UnsafeBaseUrlsAreRejected(string value)
    {
        var success = ConfigSettings.TrySet(
            CapriDefaultSettings.Create(),
            "provider.baseUrl",
            value,
            out var updated,
            out var error);

        Assert.False(success);
        Assert.NotEmpty(error);
        Assert.DoesNotContain("user:pass", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://api.openai.com/v1", updated.Provider.BaseUrl);
    }

    [Fact]
    public void SwitchingProviderUpdatesCatalogDefaultBaseUrl()
    {
        var current = CapriDefaultSettings.Create();

        var success = ConfigSettings.TrySet(
            current,
            "provider.id",
            "ollama",
            out var updated,
            out _);

        Assert.True(success);
        Assert.Equal("ollama", updated.Provider.ProviderId);
        Assert.Equal("http://localhost:11434/v1", updated.Provider.BaseUrl);
    }

    [Fact]
    public void SwitchingProviderPreservesCustomBaseUrl()
    {
        var current = CapriDefaultSettings.Create() with
        {
            Provider = CapriDefaultSettings.Create().Provider with
            {
                BaseUrl = "https://custom.example.com/v1",
            },
        };

        var success = ConfigSettings.TrySet(
            current,
            "provider.id",
            "ollama",
            out var updated,
            out _);

        Assert.True(success);
        Assert.Equal("https://custom.example.com/v1", updated.Provider.BaseUrl);
    }

    [Fact]
    public void AllowCloudRecordsAndClearsConsent()
    {
        var current = CapriDefaultSettings.Create();

        Assert.True(ConfigSettings.TrySet(
            current,
            "provider.allowCloud",
            "true",
            out var enabled,
            out _));
        Assert.True(enabled.Provider.AllowCloudInference);
        Assert.NotNull(enabled.Provider.CloudConsentRecordedUtc);

        Assert.True(ConfigSettings.TrySet(
            enabled,
            "provider.allowCloud",
            "false",
            out var disabled,
            out _));
        Assert.False(disabled.Provider.AllowCloudInference);
        Assert.Null(disabled.Provider.CloudConsentRecordedUtc);
    }

    [Fact]
    public void BaseUrlDisplayRedactsUserInfoAndQuery()
    {
        var display = SafeUrl.ForDisplay("https://user:pass@example.com/v1?api_key=secret#frag");

        Assert.DoesNotContain("user", display, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", display, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", display, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", display, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://example.com/v1", display, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUrlDisplayDoesNotEchoInput()
    {
        Assert.Equal("(invalid or hidden URL)", SafeUrl.ForDisplay("sk-test-secret-value"));
    }
}
