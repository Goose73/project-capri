using System.Diagnostics;
using System.IO;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Security;

public sealed class DeveloperGitInspectorTests
{
    [Fact]
    public async Task GitInspectionRequiresExplicitPerRequestReadAuthorization()
    {
        using var fixture = new GitFixture();
        var inspector = fixture.CreateInspector();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => inspector.GetStatusAsync(fixture.SessionId, authorized: false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => inspector.GetDiffAsync(fixture.SessionId, "README.md", authorized: false));
    }

    [Fact]
    public async Task GitPreviewShowsStagedAndUnstagedChangesWithoutChangingFiles()
    {
        using var fixture = new GitFixture();
        if (!GitFixture.GitInstalled) return;
        fixture.InitializeRepository();
        File.WriteAllText(fixture.Readme, "staged version\n");
        fixture.RunGit("add", "--", "README.md");
        File.WriteAllText(fixture.Readme, "unstaged version\n");

        var inspector = fixture.CreateInspector();
        var status = await inspector.GetStatusAsync(fixture.SessionId, authorized: true);
        var preview = await inspector.GetDiffAsync(
            fixture.SessionId, "README.md", authorized: true);

        Assert.Contains("README.md", status.Output);
        Assert.Contains("staged version", preview.StagedDiff);
        Assert.Contains("unstaged version", preview.UnstagedDiff);
        Assert.Equal("unstaged version\n", File.ReadAllText(fixture.Readme));
        Assert.DoesNotContain("diff --git", status.Output);
    }

    [Fact]
    public async Task GitDiffRefusesSecretOrOutOfProjectFiles()
    {
        using var fixture = new GitFixture();
        if (!GitFixture.GitInstalled) return;
        fixture.InitializeRepository();
        File.WriteAllText(Path.Combine(fixture.Project, "passwords.txt"), "PRIVATE");
        var inspector = fixture.CreateInspector();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => inspector.GetDiffAsync(fixture.SessionId, "passwords.txt", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => inspector.GetDiffAsync(fixture.SessionId, "../secrets.txt", true));
    }

    [Fact]
    public async Task UntrackedPrivateFilenamesAreExcludedFromGitStatus()
    {
        using var fixture = new GitFixture();
        if (!GitFixture.GitInstalled) return;
        fixture.InitializeRepository();
        File.WriteAllText(Path.Combine(fixture.Project, "sensitive-untracked-data.txt"), "PRIVATE");

        var status = await fixture.CreateInspector().GetStatusAsync(fixture.SessionId, true);

        Assert.DoesNotContain("sensitive-untracked-data.txt", status.Output);
    }

    private sealed class GitFixture : IDisposable
    {
        private readonly TempDirectory _temp = new();
        private readonly FileDeveloperSessionStore _sessions;
        private readonly FileDeveloperWorkspaceReader _reader;

        public GitFixture()
        {
            Project = Path.Combine(_temp.Path, "source-project");
            Directory.CreateDirectory(Project);
            Readme = Path.Combine(Project, "README.md");
            File.WriteAllText(Readme, "original version\n");
            _sessions = new FileDeveloperSessionStore(new CapriPaths(
                Path.Combine(_temp.Path, "capri-data")));
            _reader = new FileDeveloperWorkspaceReader(_sessions);
            SessionId = _sessions.Create(Project).Id;
        }

        public string Project { get; }
        public string Readme { get; }
        public Guid SessionId { get; }
        public static bool GitInstalled =>
            File.Exists(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));

        public DeveloperGitInspector CreateInspector() => new(_sessions, _reader);

        public void InitializeRepository()
        {
            RunGit("init", "-q");
            RunGit("add", "--", "README.md");
            RunGit("-c", "user.name=CapriTests", "-c", "user.email=test@example.invalid",
                "commit", "-q", "-m", "Initial");
        }

        public void RunGit(params string[] args)
        {
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
                WorkingDirectory = Project,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ??
                throw new InvalidOperationException("Git test process did not start.");
            if (!process.WaitForExit(10_000) || process.ExitCode != 0)
                throw new InvalidOperationException("Git test repository setup failed.");
        }

        public void Dispose() => _temp.Dispose();
    }
}
