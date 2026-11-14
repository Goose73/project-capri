using System.IO;
using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.Cli;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class FileDeveloperSessionStoreTests
{
    [Fact]
    public void ListingEmptySessionsDoesNotCreateFiles()
    {
        using var temp = new TempDirectory();
        var store = new FileDeveloperSessionStore(new CapriPaths(temp.Path));

        Assert.Empty(store.List());
        Assert.Null(store.Find(Guid.NewGuid()));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "developer")));
    }

    [Fact]
    public void CreatingSessionOnlyWritesPrivateMetadataAndCanBeOpenedFromAnotherStore()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "My Project");
        Directory.CreateDirectory(project);
        var paths = new CapriPaths(temp.Path);
        var store = new FileDeveloperSessionStore(paths);

        var created = store.Create(project);
        var fromAnotherProcess = new FileDeveloperSessionStore(paths);
        var found = fromAnotherProcess.Find(created.Id);

        Assert.NotNull(found);
        Assert.Equal("My Project", found.DisplayName);
        Assert.Equal(Path.GetFullPath(project), found.WorkspaceDirectory);
        Assert.Equal(created, Assert.Single(fromAnotherProcess.List()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(project));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(temp.Path, "developer", "sessions"), "*.json"));
    }

    [Fact]
    public void RejectsMissingProjectWithoutCreatingMetadata()
    {
        using var temp = new TempDirectory();
        var store = new FileDeveloperSessionStore(new CapriPaths(temp.Path));

        Assert.Throws<DirectoryNotFoundException>(() => store.Create(Path.Combine(temp.Path, "does-not-exist")));
        Assert.Empty(store.List());
    }

    [Fact]
    public void InvalidJsonMetadataFailsClosedInsteadOfSilentlyDroppingSession()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "Project");
        Directory.CreateDirectory(folder);
        var store = new FileDeveloperSessionStore(new CapriPaths(temp.Path));
        var session = store.Create(folder);
        var metadata = Path.Combine(temp.Path, "developer", "sessions", session.Id.ToString("N") + ".json");
        File.WriteAllText(metadata, "{not json");

        Assert.Throws<InvalidDataException>(() => store.List());
        Assert.Throws<InvalidDataException>(() => store.Find(session.Id));
    }

    [Fact]
    public void LongControlCharacterNameIsSanitizedInStoredMetadata()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "Project");
        Directory.CreateDirectory(project);
        var store = new FileDeveloperSessionStore(new CapriPaths(temp.Path));

        var session = store.Create(project, new string('a', 100) + "\nsecret");

        Assert.Equal(80, session.DisplayName.Length);
        Assert.DoesNotContain('\n', session.DisplayName);
    }

    [Fact]
    public void CliJsonListAndShowShareTheExactPersistedSession()
    {
        using var temp = new TempDirectory();
        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var storage = new FileDeveloperSessionStore(new CapriPaths(temp.Path));

        var createConsole = new FakeCliConsole();
        var create = new DevCommand(createConsole, storage);
        Assert.Equal(CliExitCodes.Success, create.Execute(CliParser.Parse(["dev", "new", folder, "--json"])));
        using var saved = JsonDocument.Parse(createConsole.Out);
        Assert.True(saved.RootElement.GetProperty("ok").GetBoolean());
        var id = saved.RootElement.GetProperty("session").GetProperty("id").GetGuid();

        var listConsole = new FakeCliConsole();
        Assert.Equal(CliExitCodes.Success,
            new DevCommand(listConsole, new FileDeveloperSessionStore(new CapriPaths(temp.Path)))
                .Execute(CliParser.Parse(["dev", "list", "--json"])));
        using var listed = JsonDocument.Parse(listConsole.Out);
        Assert.Equal(id, Assert.Single(listed.RootElement.GetProperty("sessions").EnumerateArray())
            .GetProperty("id").GetGuid());

        var showConsole = new FakeCliConsole();
        Assert.Equal(CliExitCodes.Success,
            new DevCommand(showConsole, storage).Execute(CliParser.Parse(["dev", "show", id.ToString(), "--json"])));
        using var shown = JsonDocument.Parse(showConsole.Out);
        Assert.Equal(folder, shown.RootElement.GetProperty("session").GetProperty("workspaceDirectory").GetString());
    }

    [Fact]
    public void CliBadIdsAndMissingFoldersReturnSafeJsonError()
    {
        using var temp = new TempDirectory();
        var store = new FileDeveloperSessionStore(new CapriPaths(temp.Path));
        var console = new FakeCliConsole();

        var result = new DevCommand(console, store).Execute(
            CliParser.Parse(["dev", "new", Path.Combine(temp.Path, "absent"), "--json"]));

        Assert.Equal(CliExitCodes.Usage, result);
        using var json = JsonDocument.Parse(console.Out);
        Assert.Equal("missing_workspace", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(store.List());
    }
}
