using System.IO;
using Capri.Core.Abstractions;
using Capri.Core.Providers;
using Capri.Providers;

namespace Capri.Tests.Security;

public sealed class CredentialMigrationTests
{
    private const string LegacyKey = ProviderCredentials.LegacyApiKeyCredentialKey;
    private const string OpenAiProfileKey = "provider.openai.apiKey";

    [Fact]
    public void CredentialKeysUseCanonicalProfileIds()
    {
        Assert.Equal("provider.apiKey", OpenAiCompatibleChatProvider.ApiKeyCredentialKey);
        Assert.Equal("provider.apiKey", LegacyKey);
        Assert.Equal(
            "provider.ollama.apiKey",
            ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.Ollama));
        Assert.Equal(
            "provider.lmstudio.apiKey",
            ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.LmStudio));
        Assert.Equal(
            "provider.openrouter.apiKey",
            ProviderCredentials.GetApiKeyCredentialKey("OpenRouter"));
        Assert.Equal(
            "provider.openai.apiKey",
            ProviderCredentials.GetApiKeyCredentialKey("  OPENAI  "));
        Assert.Equal(
            "provider.custom-openai.apiKey",
            ProviderCredentials.GetApiKeyCredentialKey(" Custom-OpenAI "));
    }

    [Fact]
    public async Task ResolveApiKeyCopiesVerifiesAndDeletesLegacyCredential()
    {
        var store = new TestCredentialStore();
        store.Seed(LegacyKey, "legacy-key");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: true);

        Assert.Equal("legacy-key", key);
        Assert.Equal(new[] { OpenAiProfileKey }, store.Gets.Take(1));
        Assert.Equal(LegacyKey, store.Gets[1]);
        Assert.Equal(new[] { OpenAiProfileKey }, store.Sets);
        Assert.Contains(OpenAiProfileKey, store.HasChecks);
        Assert.Equal(new[] { LegacyKey }, store.Deletes);
        Assert.Equal("legacy-key", store.Peek(OpenAiProfileKey));
        Assert.False(store.Contains(LegacyKey));
    }

    [Fact]
    public async Task ExistingProfileCredentialWinsWithoutReadingLegacyKey()
    {
        var store = new TestCredentialStore();
        store.Seed(LegacyKey, "legacy-key");
        store.Seed(OpenAiProfileKey, "profile-key");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: true);

        Assert.Equal("profile-key", key);
        Assert.DoesNotContain(LegacyKey, store.Gets);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
        Assert.Equal("legacy-key", store.Peek(LegacyKey));
    }

    [Fact]
    public async Task LegacyCredentialIsIgnoredWhenFallbackIsNotAllowed()
    {
        var store = new TestCredentialStore();
        store.Seed(LegacyKey, "legacy-key");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: false);

        Assert.Null(key);
        Assert.DoesNotContain(LegacyKey, store.Gets);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
        Assert.Equal("legacy-key", store.Peek(LegacyKey));
    }

    [Fact]
    public async Task FailedVerificationKeepsLegacyCredential()
    {
        var store = new TestCredentialStore { ReportProfileKeyMissing = true };
        store.Seed(LegacyKey, "legacy-key");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: true);

        Assert.Equal("legacy-key", key);
        Assert.Equal(new[] { OpenAiProfileKey }, store.Sets);
        Assert.Empty(store.Deletes);
        Assert.Equal("legacy-key", store.Peek(LegacyKey));
        Assert.Equal("legacy-key", store.Peek(OpenAiProfileKey));
    }

    [Fact]
    public async Task FailedCopyKeepsLegacyCredentialAndUsesItForTheRequest()
    {
        var store = new TestCredentialStore { FailSet = true };
        store.Seed(LegacyKey, "legacy-key");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: true);

        Assert.Equal("legacy-key", key);
        Assert.Empty(store.Deletes);
        Assert.Equal("legacy-key", store.Peek(LegacyKey));
        Assert.False(store.Contains(OpenAiProfileKey));
    }

    [Fact]
    public async Task WhitespaceLegacyCredentialIsTreatedAsAbsent()
    {
        var store = new TestCredentialStore();
        store.Seed(LegacyKey, "   ");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: true);

        Assert.Null(key);
        Assert.Contains(LegacyKey, store.Gets);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
    }

    [Fact]
    public async Task ProfileCredentialWhitespaceIsTrimmed()
    {
        var store = new TestCredentialStore();
        store.Seed(OpenAiProfileKey, "  profile-key  ");

        var key = await CredentialMigration.ResolveApiKeyAsync(
            store,
            OpenAiProfileKey,
            allowLegacyFallback: true);

        Assert.Equal("profile-key", key);
    }

    private sealed class TestCredentialStore : ISecureCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public List<string> Gets { get; } = [];

        public List<string> HasChecks { get; } = [];

        public List<string> Sets { get; } = [];

        public List<string> Deletes { get; } = [];

        public bool FailSet { get; init; }

        public bool ReportProfileKeyMissing { get; init; }

        public void Seed(string key, string value)
        {
            _values[key] = value;
        }

        public string? Peek(string key)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }

        public bool Contains(string key)
        {
            return _values.ContainsKey(key);
        }

        public Task<bool> HasAsync(string key, CancellationToken cancellationToken = default)
        {
            HasChecks.Add(key);
            if (ReportProfileKeyMissing
                && !string.Equals(key, LegacyKey, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_values.ContainsKey(key));
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            Gets.Add(key);
            return Task.FromResult(Peek(key));
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            Sets.Add(key);
            if (FailSet)
            {
                throw new IOException("simulated write failure");
            }

            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Deletes.Add(key);
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
