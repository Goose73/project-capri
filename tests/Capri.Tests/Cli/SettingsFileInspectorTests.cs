using System.IO;
using Capri.Cli;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Cli;

public sealed class SettingsFileInspectorTests
{
    [Fact]
    public void MissingFileIsReported()
    {
        using var temp = new TempDirectory();

        var inspection = SettingsFileInspector.Inspect(new CapriPaths(temp.Path));

        Assert.Equal(SettingsFileState.Missing, inspection.State);
    }

    [Fact]
    public void EmptyFileIsReported()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "settings.json"), []);

        var inspection = SettingsFileInspector.Inspect(new CapriPaths(temp.Path));

        Assert.Equal(SettingsFileState.Empty, inspection.State);
    }

    [Fact]
    public void MalformedFileIsReported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{ not valid json");

        var inspection = SettingsFileInspector.Inspect(new CapriPaths(temp.Path));

        Assert.Equal(SettingsFileState.Malformed, inspection.State);
    }

    [Fact]
    public void ValidFileIsReadable()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "settings.json"),
            """{ "schemaVersion": 2, "provider": { "providerId": "ollama", "chatModel": "llama3.2" } }""");

        var inspection = SettingsFileInspector.Inspect(new CapriPaths(temp.Path));

        Assert.Equal(SettingsFileState.Readable, inspection.State);
    }
}
