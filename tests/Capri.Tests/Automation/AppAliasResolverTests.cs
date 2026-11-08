using System.IO;
using Capri.Automation.Apps;

namespace Capri.Tests.Automation;

public sealed class AppAliasResolverTests
{
    private static readonly string WindowsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Theory]
    [InlineData("notepad")]
    [InlineData("NotePad")]
    [InlineData("NOTEPAD")]
    [InlineData("notepad.exe")]
    public async Task ResolvesNotepadCaseInsensitively(string alias)
    {
        var resolver = new AppAliasResolver();

        var target = await resolver.ResolveAsync(alias);

        Assert.NotNull(target);
        Assert.Equal("Notepad", target.Name);
        Assert.Equal("notepad", target.ProcessName);
        Assert.Equal(
            Path.Combine(WindowsDirectory, "System32", "notepad.exe"),
            target.ExecutablePath,
            ignoreCase: true);
    }

    [Theory]
    [InlineData("calculator", "calc.exe")]
    [InlineData("calc", "calc.exe")]
    [InlineData("paint", "mspaint.exe")]
    [InlineData("mspaint", "mspaint.exe")]
    public async Task ResolvesCalculatorAndPaintAliases(string alias, string executableName)
    {
        var resolver = new AppAliasResolver();

        var target = await resolver.ResolveAsync(alias);

        Assert.NotNull(target);
        Assert.Equal(
            Path.Combine(WindowsDirectory, "System32", executableName),
            target.ExecutablePath,
            ignoreCase: true);
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("file explorer")]
    [InlineData("files")]
    public async Task ResolvesExplorerAliases(string alias)
    {
        var resolver = new AppAliasResolver();

        var target = await resolver.ResolveAsync(alias);

        Assert.NotNull(target);
        Assert.Equal(
            Path.Combine(WindowsDirectory, "explorer.exe"),
            target.ExecutablePath,
            ignoreCase: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData(@"..\cmd")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"%TEMP%\evil.exe")]
    [InlineData("cmd")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("wscript")]
    [InlineData("mshta")]
    [InlineData("rundll32")]
    public async Task UnknownPathLikeAndForbiddenAliasesReturnNull(string alias)
    {
        var resolver = new AppAliasResolver();

        var target = await resolver.ResolveAsync(alias);

        Assert.Null(target);
    }

    [Fact]
    public void EveryDefaultAliasUsesAnAllowedAbsolutePath()
    {
        var aliases = AppAliasResolver.CreateDefaultAliases();

        Assert.NotEmpty(aliases);
        foreach (var target in aliases.Values)
        {
            Assert.True(
                AppPathPolicy.TryValidatePath(target.ExecutablePath, out _, out var reason),
                $"{target.Name}: {reason}");
            Assert.False(AppPathPolicy.IsForbiddenExecutable(target.ExecutablePath));
        }
    }

    [Fact]
    public async Task CancelledResolutionThrows()
    {
        var resolver = new AppAliasResolver();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync("notepad", cts.Token).AsTask());
    }
}
