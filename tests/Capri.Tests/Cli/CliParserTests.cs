using Capri.Cli;

namespace Capri.Tests.Cli;

public sealed class CliParserTests
{
    [Fact]
    public void NoArgumentsSelectsDashboard()
    {
        var invocation = CliParser.Parse(Array.Empty<string>());

        Assert.Equal(CliCommandKind.None, invocation.Kind);
        Assert.True(invocation.IsValid);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void HelpVariantsAreRecognized(string argument)
    {
        var invocation = CliParser.Parse([argument]);

        Assert.Equal(CliCommandKind.Help, invocation.Kind);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void StatusAcceptsJson()
    {
        var invocation = CliParser.Parse(["status", "--json"]);

        Assert.Equal(CliCommandKind.Status, invocation.Kind);
        Assert.True(invocation.Json);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void DoctorRejectsRepairOption()
    {
        var invocation = CliParser.Parse(["doctor", "--repair"]);

        Assert.False(invocation.IsValid);
        Assert.Equal(CliCommandKind.Unknown, invocation.Kind);
    }

    [Fact]
    public void SetupAcceptsRepair()
    {
        var invocation = CliParser.Parse(["setup", "--repair"]);

        Assert.Equal(CliCommandKind.Setup, invocation.Kind);
        Assert.True(invocation.Repair);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void GuiAliasIsRecognized()
    {
        var invocation = CliParser.Parse(["gui"]);

        Assert.Equal(CliCommandKind.Gui, invocation.Kind);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void ConfigListAcceptsJsonAndKeepsSubcommand()
    {
        var invocation = CliParser.Parse(["config", "list", "--json"]);

        Assert.Equal(CliCommandKind.Config, invocation.Kind);
        Assert.Equal(["list"], invocation.Arguments);
        Assert.True(invocation.Json);
    }

    [Fact]
    public void ConfigGetKeepsSettingPositional()
    {
        var invocation = CliParser.Parse(["config", "get", "provider.model", "--json"]);

        Assert.Equal(CliCommandKind.Config, invocation.Kind);
        Assert.Equal(["get", "provider.model"], invocation.Arguments);
        Assert.True(invocation.Json);
    }

    [Fact]
    public void ConfigSetRequiresSettingAndValue()
    {
        var invalid = CliParser.Parse(["config", "set", "provider.model"]);
        var valid = CliParser.Parse(["config", "set", "provider.model", "llama3.2"]);

        Assert.False(invalid.IsValid);
        Assert.True(valid.IsValid);
        Assert.Equal(["set", "provider.model", "llama3.2"], valid.Arguments);
    }

    [Fact]
    public void UnknownCommandDoesNotEchoRawInput()
    {
        const string sentinel = "sk-test-secret-value";
        var invocation = CliParser.Parse([sentinel]);

        Assert.False(invocation.IsValid);
        Assert.Equal(CliCommandKind.Unknown, invocation.Kind);
        Assert.DoesNotContain(sentinel, invocation.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigHelpRoutesToHelpTopic()
    {
        var invocation = CliParser.Parse(["config", "--help"]);

        Assert.Equal(CliCommandKind.Help, invocation.Kind);
        Assert.Equal("config", invocation.Topic);
    }

    [Fact]
    public void IsKnownCommandIsCaseInsensitive()
    {
        Assert.True(CliParser.IsKnownCommand("STATUS"));
        Assert.False(CliParser.IsKnownCommand("unknown"));
        Assert.False(CliParser.IsKnownCommand(null));
    }

    [Theory]
    [InlineData("models")]
    [InlineData("ask")]
    [InlineData("chat")]
    [InlineData("providers")]
    public void NewM3CommandsAreKnown(string command)
    {
        Assert.True(CliParser.IsKnownCommand(command));
    }

    [Fact]
    public void DevDefaultProvidesGuidedHelp()
    {
        var invocation = CliParser.Parse(["dev"]);
        Assert.Equal(CliCommandKind.Help, invocation.Kind);
        Assert.Equal("dev", invocation.Topic);
    }

    [Fact]
    public void DevSessionsParseWithoutSurpriseActions()
    {
        var created = CliParser.Parse(["dev", "new", "C:\\Code\\Demo", "--json"]);
        var list = CliParser.Parse(["dev", "list", "--json"]);
        var invalid = CliParser.Parse(["dev", "new", "--yes"]);
        Assert.Equal(CliCommandKind.Dev, created.Kind);
        Assert.Equal(["new", "C:\\Code\\Demo"], created.Arguments);
        Assert.True(created.Json);
        Assert.Equal(CliCommandKind.Dev, list.Kind);
        Assert.Equal(["list"], list.Arguments);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void DevChatHistoryAndReadPermissionsAreParsedExplicitly()
    {
        var ask = CliParser.Parse(["dev", "ask", "fake-session", "Hello", "--json"]);
        var history = CliParser.Parse(["dev", "history", "fake-session"]);
        var names = CliParser.Parse(["dev", "files", "fake-session", "src", "--allow-read"]);
        var preview = CliParser.Parse(["dev", "file", "fake-session", "src/main.cs", "--allow-read", "--json"]);
        var rejected = CliParser.Parse(["dev", "ask", "fake-session", "Hello", "--allow-read"]);
        var missing = CliParser.Parse(["dev", "file", "fake-session"]);

        Assert.True(ask.IsValid);
        Assert.Equal(["ask", "fake-session", "Hello"], ask.Arguments);
        Assert.True(ask.Json);
        Assert.True(history.IsValid);
        Assert.True(names.AllowRead);
        Assert.False(names.Json);
        Assert.Equal(["files", "fake-session", "src"], names.Arguments);
        Assert.True(preview.IsValid);
        Assert.True(preview.AllowRead);
        Assert.True(preview.Json);
        Assert.False(rejected.IsValid);
        Assert.False(missing.IsValid);
    }

    [Fact]
    public void DeveloperSourceSharingRequiresAllThreeExplicitFlags()
    {
        var accepted = CliParser.Parse([
            "dev", "ask", "session-id", "Explain this", "--context", "src/main.cs",
            "--allow-read", "--send-context", "--json"]);
        Assert.True(accepted.IsValid);
        Assert.Equal("src/main.cs", accepted.ContextFile);
        Assert.True(accepted.AllowRead);
        Assert.True(accepted.SendContext);
        Assert.True(accepted.Json);
        Assert.Equal(["ask", "session-id", "Explain this"], accepted.Arguments);

        Assert.False(CliParser.Parse([
            "dev", "ask", "session", "Explain", "--context", "src/main.cs"]).IsValid);
        Assert.False(CliParser.Parse([
            "dev", "ask", "session", "Explain", "--context", "src/main.cs", "--allow-read"]).IsValid);
        Assert.False(CliParser.Parse([
            "dev", "ask", "session", "Explain", "--send-context"]).IsValid);
        Assert.False(CliParser.Parse([
            "dev", "ask", "session", "Explain", "--allow-read"]).IsValid);
        Assert.False(CliParser.Parse([
            "dev", "files", "session", "--send-context"]).IsValid);
        Assert.False(CliParser.Parse([
            "dev", "ask", "session", "Explain", "--context"]).IsValid);
        Assert.False(CliParser.Parse([
            "dev", "ask", "session", "Explain", "--context", "--json"]).IsValid);
    }

    [Fact]
    public void ModelsListParsesJson()
    {
        var invocation = CliParser.Parse(["models", "list", "--json"]);

        Assert.Equal(CliCommandKind.Models, invocation.Kind);
        Assert.Equal(["list"], invocation.Arguments);
        Assert.True(invocation.Json);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void ModelsRecommendParses()
    {
        var invocation = CliParser.Parse(["models", "recommend"]);

        Assert.Equal(CliCommandKind.Models, invocation.Kind);
        Assert.Equal(["recommend"], invocation.Arguments);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void ModelsRecommendAcceptsExplicitHardwareConsent()
    {
        var invocation = CliParser.Parse(["models", "recommend", "--yes", "--json"]);

        Assert.True(invocation.IsValid);
        Assert.True(invocation.Yes);
        Assert.True(invocation.Json);
        Assert.Equal(["recommend"], invocation.Arguments);
    }

    [Fact]
    public void ModelsPullParsesYesFlag()
    {
        var invocation = CliParser.Parse(["models", "pull", "qwen3.5:4b", "--yes"]);

        Assert.Equal(CliCommandKind.Models, invocation.Kind);
        Assert.Equal(["pull", "qwen3.5:4b"], invocation.Arguments);
        Assert.True(invocation.Yes);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void ModelsPullRequiresModelName()
    {
        var invocation = CliParser.Parse(["models", "pull"]);

        Assert.False(invocation.IsValid);
        Assert.Equal(CliCommandKind.Unknown, invocation.Kind);
    }

    [Fact]
    public void ModelsUseParsesModelName()
    {
        var invocation = CliParser.Parse(["models", "use", "qwen3.5:4b"]);

        Assert.Equal(CliCommandKind.Models, invocation.Kind);
        Assert.Equal(["use", "qwen3.5:4b"], invocation.Arguments);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void AskParsesQuestionAndJson()
    {
        var invocation = CliParser.Parse(["ask", "How are you?", "--json"]);

        Assert.Equal(CliCommandKind.Ask, invocation.Kind);
        Assert.Equal(["How are you?"], invocation.Arguments);
        Assert.True(invocation.Json);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void AskRequiresQuestion()
    {
        var invocation = CliParser.Parse(["ask"]);

        Assert.False(invocation.IsValid);
        Assert.Equal(CliCommandKind.Unknown, invocation.Kind);
    }

    [Fact]
    public void ChatParses()
    {
        var invocation = CliParser.Parse(["chat"]);

        Assert.Equal(CliCommandKind.Chat, invocation.Kind);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void ProvidersTestParsesJson()
    {
        var invocation = CliParser.Parse(["providers", "test", "--json"]);

        Assert.Equal(CliCommandKind.Providers, invocation.Kind);
        Assert.Equal(["test"], invocation.Arguments);
        Assert.True(invocation.Json);
        Assert.True(invocation.IsValid);
    }

    [Fact]
    public void ProvidersUnknownSubcommandIsUsageError()
    {
        var invocation = CliParser.Parse(["providers", "list"]);

        Assert.False(invocation.IsValid);
        Assert.Equal(CliCommandKind.Unknown, invocation.Kind);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("tasks")]
    public void M4TaskCommandsAreKnown(string command)
    {
        Assert.True(CliParser.IsKnownCommand(command));
    }

    [Fact]
    public void TaskRequestParsesUnquotedRemainder()
    {
        var invocation = CliParser.Parse(["task", "create", "a", "note"]);
        Assert.Equal(CliCommandKind.Task, invocation.Kind);
        Assert.True(invocation.IsValid);
        Assert.Equal(["create a note"], invocation.Arguments);
        Assert.False(invocation.Json);
    }

    [Fact]
    public void TaskStopParsesWithJson()
    {
        var invocation = CliParser.Parse(["task", "stop", "--json"]);
        Assert.Equal(CliCommandKind.Task, invocation.Kind);
        Assert.Equal(["stop"], invocation.Arguments);
        Assert.True(invocation.Json);
    }

    [Fact]
    public void TaskStatusRequiresId()
    {
        var invalid = CliParser.Parse(["task", "status"]);
        var valid = CliParser.Parse(["task", "status", "00000000-0000-0000-0000-000000000001", "--json"]);
        Assert.False(invalid.IsValid);
        Assert.True(valid.IsValid);
        Assert.Equal(["status", "00000000-0000-0000-0000-000000000001"], valid.Arguments);
        Assert.True(valid.Json);
    }

    [Fact]
    public void TasksParsesJson()
    {
        var invocation = CliParser.Parse(["tasks", "--json"]);
        Assert.Equal(CliCommandKind.Tasks, invocation.Kind);
        Assert.True(invocation.Json);
        Assert.True(invocation.IsValid);
    }


    [Fact]
    public void DesktopStatusParsesAsReadOnlyTaskCommand()
    {
        var invocation = CliParser.Parse(["task", "desktop-status", "--json"]);
        Assert.True(invocation.IsValid);
        Assert.Equal(CliCommandKind.Task, invocation.Kind);
        Assert.Equal(["desktop-status"], invocation.Arguments);
        Assert.True(invocation.Json);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("--unexpected")]
    public void DesktopStatusRejectsUnexpectedArguments(string argument)
    {
        var invocation = CliParser.Parse(["task", "desktop-status", argument]);
        Assert.False(invocation.IsValid);
    }

    [Fact]
    public void TaskExecutionRejectsJson()
    {
        var invocation = CliParser.Parse(["task", "do", "something", "--json"]);
        Assert.False(invocation.IsValid);
        Assert.Contains("interactive", invocation.Error, StringComparison.OrdinalIgnoreCase);
    }
}
