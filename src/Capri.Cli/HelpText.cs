namespace Capri.Cli;

/// <summary>
/// Help text for the CLI. Kept in one place so every entry point stays
/// consistent.
/// </summary>
public static class HelpText
{
    /// <summary>Full usage text.</summary>
    public const string Full = """
        Project Capri - CLI

        Usage:
          capri                          Open the interactive dashboard
          capri status [--json]          Show settings, provider, credential, and endpoint status
          capri doctor [--json]          Run diagnostics; exits 3 when critical issues are found
          capri config                   Open the interactive settings menu
          capri config list [--json]     List non-secret settings
          capri config keys [--json]     List settings that can be read or changed
          capri config get <setting> [--json]
          capri config set <setting> <value> [--json]
          capri models list [--json]     List installed local models (Ollama)
          capri models recommend [--json]
                                         Recommend local models for this machine
          capri models pull <model> [--yes]
                                         Download a model after explicit confirmation
          capri models use <model>       Activate a model and persist provider settings
          capri ask "<question>" [--json]
                                         Send a question to the configured provider
          capri chat                     Interactive chat with the configured provider
          capri providers test [--json]  Run an actual inference test with the configured provider
          capri setup [--repair]         Interactive first-run setup/repair wizard
          capri start                    Launch the desktop app
          capri gui                      Alias for 'capri start'
          capri help [command]           Show help
          capri --version                Show the CLI version

        Safety:
          Secrets are never displayed by the CLI. Use the desktop Settings window
          to enter API keys. Config commands only accept non-secret settings.
          Cloud inference respects configured consent/guards. `capri models pull`
          never downloads without explicit confirmation.

        Exit codes:
          0   success
          1   runtime, I/O, or launch error
          2   invalid command line or value
          3   doctor found critical issues
          130 cancelled (Ctrl+C)

        Tip: add the CLI output folder (or the repository root with capri.cmd) to PATH.
        """;

    /// <summary>Returns help for a specific command, or the full help text.</summary>
    public static string For(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return Full;
        }

        return topic.Trim().ToLowerInvariant() switch
        {
            "status" => """
                capri status [--json]

                Shows whether the settings file exists and is readable, the active
                provider/model and non-secret flags, whether an API key is present
                for the active profile (never the key itself), and best-effort
                reachability for the Ollama and LM Studio local endpoints.
                """,
            "doctor" => """
                capri doctor [--json]

                Checks settings readability, provider configuration, local endpoint
                reachability, and DPAPI credential-store availability without
                displaying credential data. Critical findings produce exit code 3;
                warnings still exit 0. Each finding includes a suggested fix.
                """,
            "config" => """
                capri config
                capri config list [--json]
                capri config keys [--json]
                capri config get <setting> [--json]
                capri config set <setting> <value> [--json]

                Only non-secret settings are accessible. Secret and credential
                settings are rejected. Use the desktop Settings window for API keys.
                Values are validated before they are persisted.
                """,
            "setup" => """
                capri setup [--repair]

                Interactive guided setup wizard. Detects hardware (best-effort),
                checks local Ollama/LM Studio reachability and installed Ollama models,
                recommends free local models, lets you choose/download a model with
                explicit confirmation (no silent downloads), runs a genuine inference
                test ("Reply with: Capri ready"), configures voice/UI/automation
                preferences in shared settings, and can run a short intro conversation.

                --repair focuses on diagnostics and reconfiguration. Local AI does not
                require an API key; secrets are never displayed; cloud consent is never
                enabled silently.

                GUI access: run `capri setup` in Windows Terminal. Desktop integration
                may launch/access the CLI setup where supported.
                """,
            "models" => """
                capri models list [--json]
                capri models recommend [--json]
                capri models pull <model> [--yes]
                capri models use <model>

                Manages local Ollama models through the local API. `list` shows
                installed models. `recommend` detects hardware locally (CPU/RAM/GPU/
                disk, best effort) and suggests compact models. `pull` shows the
                model tag/size information and requires explicit confirmation before
                download; use --yes only in non-interactive environments. `use`
                verifies the model is installed and saves it to local settings.
                """,
            "ask" => """
                capri ask "<question>" [--json]

                Sends a question to the configured chat provider/model and prints the
                response. If no provider/model is configured, use `capri models use`
                or `capri config`. Provider errors, missing local models, and consent
                guards are reported with actionable messages. --json returns safe
                metadata and text without secrets.
                """,
            "chat" => """
                capri chat

                Starts an interactive multi-turn chat session with the configured
                provider/model. Responses stream to the console. Use /exit to quit
                and Ctrl+C to cancel the current generation where possible. The
                conversation is kept in memory for the session only.
                """,
            "providers" => """
                capri providers test [--json]

                Sends an actual inference request to the configured provider
                ("Reply with: Capri ready"), reports success/failure and latency,
                and respects free-model/consent guards. No secrets are printed.
                """,
            "start" or "gui" => """
                capri start
                capri gui

                Launches the desktop application using the built Debug executable
                when present, then Release/common fallback locations, otherwise
                'dotnet run'. The desktop app owns advanced UI, automation, and
                experimental task features; CLI chat/ask use shared backend services.
                """,
            _ => Full,
        };
    }
}
