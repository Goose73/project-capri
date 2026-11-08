using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class DpapiCredentialStoreTests
{
    private const string CredentialKey = "provider.apiKey";
    private const string SecretValue = "super-secret-value-123";

    [Fact]
    public async Task RoundTripPersistsProtectedValueWithoutPlaintext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var paths = new CapriPaths(Path.Combine(temp.Path, "capri"));
        using var store = new DpapiCredentialStore(paths);

        await store.SetAsync(CredentialKey, SecretValue);

        Assert.True(await store.HasAsync(CredentialKey));
        Assert.Equal(SecretValue, await store.GetAsync(CredentialKey));
        Assert.True(File.Exists(paths.SecretsFile));

        var fileContents = await File.ReadAllTextAsync(paths.SecretsFile);
        Assert.DoesNotContain(SecretValue, fileContents, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(fileContents);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.True(document.RootElement.GetProperty("entries").TryGetProperty(CredentialKey, out _));
    }

    [Fact]
    public async Task MissingFileAndUnknownKeyReturnAbsent()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new DpapiCredentialStore(paths);

        Assert.False(await store.HasAsync("missing"));
        Assert.Null(await store.GetAsync("missing"));
        Assert.False(File.Exists(paths.SecretsFile));
    }

    [Fact]
    public async Task DeleteRemovesOnlyRequestedCredential()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new DpapiCredentialStore(paths);

        await store.SetAsync(CredentialKey, SecretValue);
        await store.SetAsync("other.provider", "other-value");

        await store.DeleteAsync(CredentialKey);

        Assert.Null(await store.GetAsync(CredentialKey));
        Assert.Equal("other-value", await store.GetAsync("other.provider"));
    }

    [Fact]
    public async Task CorruptFileFailsClosed()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        Directory.CreateDirectory(temp.Path);
        await File.WriteAllTextAsync(paths.SecretsFile, "{ not valid json");
        using var store = new DpapiCredentialStore(paths);

        Assert.False(await store.HasAsync(CredentialKey));
        Assert.Null(await store.GetAsync(CredentialKey));
    }

    [Fact]
    public async Task TamperedCiphertextFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        Directory.CreateDirectory(temp.Path);

        var tamperedCiphertext = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);
        var document = new
        {
            version = 1,
            entries = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CredentialKey] = tamperedCiphertext,
            },
        };

        await File.WriteAllTextAsync(
            paths.SecretsFile,
            JsonSerializer.Serialize(document));

        using var store = new DpapiCredentialStore(paths);

        Assert.Null(await store.GetAsync(CredentialKey));
        Assert.False(await store.HasAsync(CredentialKey));
    }

    [Fact]
    public async Task EachKeyUsesIndependentEntropy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        using var store = new DpapiCredentialStore(paths);

        await store.SetAsync("provider.one", "same-value");
        await store.SetAsync("provider.two", "same-value");

        Assert.Equal("same-value", await store.GetAsync("provider.one"));
        Assert.Equal("same-value", await store.GetAsync("provider.two"));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SecretsFile));
        var entries = document.RootElement.GetProperty("entries");
        Assert.NotEqual(
            entries.GetProperty("provider.one").GetString(),
            entries.GetProperty("provider.two").GetString());
    }

    [Fact]
    public async Task SetCreatesMissingDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "nested", "capri");
        var paths = new CapriPaths(root);
        using var store = new DpapiCredentialStore(paths);

        await store.SetAsync(CredentialKey, SecretValue);

        Assert.True(Directory.Exists(root));
        Assert.True(File.Exists(paths.SecretsFile));
    }

    [Fact]
    public void ProtectUsesCurrentUserScope()
    {
        // Guards the documented DPAPI contract without touching the store file.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var entropy = "Capri.v1:test"u8.ToArray();
        var protectedBytes = ProtectedData.Protect(
            "value"u8.ToArray(),
            entropy,
            DataProtectionScope.CurrentUser);
        var plainBytes = ProtectedData.Unprotect(
            protectedBytes,
            entropy,
            DataProtectionScope.CurrentUser);

        Assert.Equal("value", System.Text.Encoding.UTF8.GetString(plainBytes));
    }
}
