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
}
