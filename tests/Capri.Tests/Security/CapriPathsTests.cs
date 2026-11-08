using System.IO;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

[Collection(EnvironmentVariableTestGroup.Name)]
public sealed class CapriPathsTests
{
    [Fact]
    public void ExplicitRootDerivesWellKnownLocations()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "capri-data");

        var paths = new CapriPaths(root);

        Assert.Equal(root, paths.Root);
        Assert.Equal(Path.Combine(root, "Logs"), paths.Logs);
        Assert.Equal(Path.Combine(root, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(root, "secrets.dat"), paths.SecretsFile);
        Assert.Equal(Path.Combine(root, "conversations"), paths.ConversationDir);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void EnvironmentOverrideSelectsDataRoot()
    {
        using var temp = new TempDirectory();
        var overrideRoot = Path.Combine(temp.Path, "env-root");
        var original = Environment.GetEnvironmentVariable(CapriPaths.DataDirectoryEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(CapriPaths.DataDirectoryEnvironmentVariable, overrideRoot);

            var paths = new CapriPaths();

            Assert.Equal(Path.GetFullPath(overrideRoot), paths.Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CapriPaths.DataDirectoryEnvironmentVariable, original);
        }
    }

    [Fact]
    public void EnsureCreatedCreatesRootLogsAndConversations()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "ensure");
        var paths = new CapriPaths(root);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.Root));
        Assert.True(Directory.Exists(paths.Logs));
        Assert.True(Directory.Exists(paths.ConversationDir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankExplicitRootIsRejected(string root)
    {
        Assert.Throws<ArgumentException>(() => new CapriPaths(root));
    }
}
