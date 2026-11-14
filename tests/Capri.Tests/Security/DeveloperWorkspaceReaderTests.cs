using System.IO;
using System.Text;
using System.Text.Json;
using Capri.Cli;
using Capri.Cli.Commands;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Security;
using Capri.Tests.Cli;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class DeveloperWorkspaceReaderTests
{
    [Fact]
    public void ReadingRequiresNewExplicitAuthorizationEvenForSavedSession()
    {
        using var fx = new Fixture();
        Assert.Throws<UnauthorizedAccessException>(() =>
            fx.Reader.List(fx.Session.Id, "", authorized: false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fx.Reader.ReadText(fx.Session.Id, "src/example.cs", authorized: false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            fx.Reader.List(Guid.Empty, "", authorized: false));
    }

    [Fact]
    public void OneDirectoryCanBeListedAndOnlyChosenFilePreviewed()
    {
        using var fx = new Fixture();
        var root = fx.Reader.List(fx.Session.Id, "", authorized: true);
        Assert.Contains(root, x => x.Name == "src" && x.IsDirectory);
        Assert.DoesNotContain(root, x => x.Name == ".git" || x.Name == "bin");
        var sourceItems = fx.Reader.List(fx.Session.Id, "src", authorized: true);
        Assert.Contains(sourceItems, x => x.RelativePath == Path.Combine("src", "example.cs"));
        Assert.DoesNotContain(sourceItems, x => x.Name == ".env" || x.Name.Contains("token"));

        Assert.Equal("class Example { }", fx.Reader.ReadText(
            fx.Session.Id, Path.Combine("src", "example.cs"), authorized: true));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("src/../outside.cs")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData(".git/config")]
    [InlineData("src/.env")]
    [InlineData("src/auth-token.json")]
    [InlineData("bin/Release/prog.cs")]
    public void EscapingAndSensitiveFilesAreRefused(string path)
    {
        using var fx = new Fixture();
        Assert.Throws<UnauthorizedAccessException>(() => fx.Reader.ReadText(
            fx.Session.Id, path, authorized: true));
    }

    [Fact]
    public void Utf8BomIsRemovedFromPreviewWithoutChangingTheSource()
    {
        using var fx = new Fixture();
        var path = Path.Combine(fx.Project, "src", "bom.cs");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'c', (byte)'s' };
        File.WriteAllBytes(path, bytes);

        Assert.Equal("cs", fx.Reader.ReadText(fx.Session.Id, "src/bom.cs", authorized: true));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void OversizedAndBinaryFilesCannotBePreviewed()
    {
        using var fx = new Fixture();
        File.WriteAllText(Path.Combine(fx.Project, "src", "huge.cs"),
            new string('x', 33 * 1024));
        File.WriteAllBytes(Path.Combine(fx.Project, "src", "binary.cs"), [0xff, 0xfe]);

        Assert.Throws<InvalidOperationException>(() =>
            fx.Reader.ReadText(fx.Session.Id, "src/huge.cs", authorized: true));
        Assert.Throws<DecoderFallbackException>(() =>
            fx.Reader.ReadText(fx.Session.Id, "src/binary.cs", authorized: true));
    }

    [Fact]
    public void NoninteractiveCliRefusesAllFileReadsWithoutAllowRead()
    {
        using var fx = new Fixture();
        var console = new FakeCliConsole();
        var command = new DevCommand(console, fx.Store, reader: fx.Reader);
        var code = command.Execute(CliParser.Parse([
            "dev", "files", fx.Session.Id.ToString(), "--json"]));

        Assert.Equal(CliExitCodes.Usage, code);
        using var json = JsonDocument.Parse(console.Out);
        Assert.Equal("consent_required",
            json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ExplicitScriptConsentReturnsOnlyApprovedSourceText()
    {
        using var fx = new Fixture();
        var console = new FakeCliConsole();
        var command = new DevCommand(console, fx.Store, reader: fx.Reader);
        var code = command.Execute(CliParser.Parse([
            "dev", "file", fx.Session.Id.ToString(), "src/example.cs",
            "--allow-read", "--json"]));

        Assert.Equal(CliExitCodes.Success, code);
        using var json = JsonDocument.Parse(console.Out);
        Assert.Equal("class Example { }",
            json.RootElement.GetProperty("content").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public string Project { get; }
        public FileDeveloperSessionStore Store { get; }
        public FileDeveloperWorkspaceReader Reader { get; }
        public DeveloperSession Session { get; }

        public Fixture()
        {
            Project = Path.Combine(_temp.Path, "source-project");
            Directory.CreateDirectory(Path.Combine(Project, "src"));
            Directory.CreateDirectory(Path.Combine(Project, "bin"));
            Directory.CreateDirectory(Path.Combine(Project, ".git"));
            File.WriteAllText(Path.Combine(Project, "src", "example.cs"), "class Example { }");
            File.WriteAllText(Path.Combine(Project, "src", ".env"), "SECRET=do-not-read");
            File.WriteAllText(Path.Combine(Project, "src", "auth-token.json"), "hidden-token");
            Store = new FileDeveloperSessionStore(new CapriPaths(_temp.Path));
            Session = Store.Create(Project);
            Reader = new FileDeveloperWorkspaceReader(Store);
        }

        public void Dispose() => _temp.Dispose();
    }
}

public sealed class DeveloperConversationStoreTests
{
    [Fact]
    public void PersistsEncryptedTextAcrossServiceInstancesWithoutPlaintext()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "demo");
        Directory.CreateDirectory(project);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var first = sessions.Create(project);
        var second = sessions.Create(project);
        var store = new FileDeveloperConversationStore(paths, sessions);

        var user = store.Append(first.Id, ChatRole.User, "Private developer phrase 8xG7u!");
        store.Append(first.Id, ChatRole.Assistant, "Reply");
        store.Append(second.Id, ChatRole.User, "Another session");

        var fresh = new FileDeveloperConversationStore(paths, new FileDeveloperSessionStore(paths));
        var entries = fresh.Read(first.Id);
        Assert.Equal([ChatRole.User, ChatRole.Assistant], entries.Select(x => x.Role));
        Assert.Equal(user.Id, entries[0].Id);
        Assert.Equal("Private developer phrase 8xG7u!", entries[0].Content);
        Assert.Single(fresh.Read(second.Id));

        var folder = Path.Combine(temp.Path, "developer", "conversations", first.Id.ToString("N"));
        foreach (var file in Directory.GetFiles(folder, "*.bin"))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.DoesNotContain("Private developer phrase 8xG7u!", Encoding.UTF8.GetString(bytes));
        }
    }

    [Fact]
    public void TamperingWithEncryptedFileFailsClosed()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "demo");
        Directory.CreateDirectory(project);
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var session = sessions.Create(project);
        var store = new FileDeveloperConversationStore(paths, sessions);
        store.Append(session.Id, ChatRole.User, "Read this message.");

        var file = Assert.Single(Directory.GetFiles(
            Path.Combine(temp.Path, "developer", "conversations", session.Id.ToString("N")), "*.bin"));
        File.WriteAllBytes(file, new byte[] { 3, 5, 7 });
        Assert.Throws<InvalidDataException>(() => store.Read(session.Id));
    }

    [Fact]
    public void RefusesUnknownSessionsAndNonChatRoles()
    {
        using var temp = new TempDirectory();
        var paths = new CapriPaths(temp.Path);
        var sessions = new FileDeveloperSessionStore(paths);
        var store = new FileDeveloperConversationStore(paths, sessions);

        Assert.Throws<InvalidOperationException>(() =>
            store.Append(Guid.NewGuid(), ChatRole.User, "Do not save me"));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "developer", "conversations")));

        var folder = Path.Combine(temp.Path, "project");
        Directory.CreateDirectory(folder);
        var session = sessions.Create(folder);
        Assert.Throws<ArgumentException>(() => store.Append(session.Id, ChatRole.System, "fake system"));
        Assert.Throws<ArgumentException>(() => store.Append(session.Id, ChatRole.User, new string('x', 8001)));
        Assert.Empty(store.Read(session.Id));
    }
}
