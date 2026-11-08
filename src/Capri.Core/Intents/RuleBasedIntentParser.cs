using Capri.Core.Abstractions;
using Capri.Core.Models;

namespace Capri.Core.Intents;

/// <summary>
/// Deterministic Phase 1 intent parser for a small, safe command vocabulary.
/// Only user-typed text is ever parsed; model output must never be routed
/// through this type.
/// </summary>
/// <remarks>
/// The parser deliberately refuses arbitrary paths, shell fragments, and
/// non-http(s) schemes. Unsupported requests fall back to a chat intent so the
/// UI can explain the limitation instead of attempting something unsafe.
/// </remarks>
public sealed class RuleBasedIntentParser : IIntentParser
{
    private const string YouTubeSearchUrl = "https://www.youtube.com/results?search_query=";
    private const string GoogleSearchUrl = "https://www.google.com/search?q=";
    private const string DuckDuckGoSearchUrl = "https://duckduckgo.com/?q=";

    private static readonly string[] ConversationalPrefixes =
    [
        "hey capri",
        "hi capri",
        "hello capri",
        "okay capri",
        "ok capri",
        "capri",
        "please",
        "could you",
        "would you",
        "can you",
        "will you",
        "hey",
        "hi",
        "hello",
        "okay",
        "ok",
    ];

    private static readonly string[] ConversationalSuffixes =
    [
        "please",
        "thanks",
        "thank you",
    ];

    private static readonly string[] LaunchVerbs =
    [
        "open ",
        "launch ",
        "start ",
        "run ",
    ];

    private static readonly string[] NavigationVerbs =
    [
        "open ",
        "go to ",
        "visit ",
        "navigate to ",
    ];

    private static readonly HashSet<string> AppAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "brave",
        "chrome",
        "edge",
        "firefox",
        "notepad",
        "calculator",
        "calc",
        "explorer",
        "file explorer",
        "files",
        "paint",
        "mspaint",
    };

    private static readonly HashSet<string> BlockedBareFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat",
        ".cmd",
        ".dll",
        ".exe",
        ".jar",
        ".js",
        ".lnk",
        ".msi",
        ".ps1",
        ".scr",
        ".vbs",
    };

    private static readonly Dictionary<string, MediaCommand> MediaPhrases = new(StringComparer.Ordinal)
    {
        ["play"] = MediaCommand.PlayPause,
        ["pause"] = MediaCommand.PlayPause,
        ["toggle"] = MediaCommand.PlayPause,
        ["resume"] = MediaCommand.PlayPause,
        ["play pause"] = MediaCommand.PlayPause,
        ["play/pause"] = MediaCommand.PlayPause,
        ["toggle playback"] = MediaCommand.PlayPause,
        ["play music"] = MediaCommand.PlayPause,
        ["pause music"] = MediaCommand.PlayPause,
        ["resume music"] = MediaCommand.PlayPause,

        ["next"] = MediaCommand.Next,
        ["skip"] = MediaCommand.Next,
        ["next track"] = MediaCommand.Next,
        ["skip track"] = MediaCommand.Next,
        ["next song"] = MediaCommand.Next,
        ["skip song"] = MediaCommand.Next,
        ["next video"] = MediaCommand.Next,

        ["previous"] = MediaCommand.Previous,
        ["back"] = MediaCommand.Previous,
        ["previous track"] = MediaCommand.Previous,
        ["previous song"] = MediaCommand.Previous,
        ["previous video"] = MediaCommand.Previous,
        ["last track"] = MediaCommand.Previous,

        ["stop"] = MediaCommand.Stop,
        ["stop music"] = MediaCommand.Stop,
        ["stop playback"] = MediaCommand.Stop,
        ["stop the music"] = MediaCommand.Stop,
    };

    private static readonly SearchSpec[] SearchSpecs =
    [
        new("search youtube for ", YouTubeSearchUrl, 1.0),
        new("search on youtube for ", YouTubeSearchUrl, 1.0),
        new("youtube search for ", YouTubeSearchUrl, 0.95),
        new("search google for ", GoogleSearchUrl, 1.0),
        new("search on google for ", GoogleSearchUrl, 1.0),
        new("google search for ", GoogleSearchUrl, 0.95),
        new("search for ", DuckDuckGoSearchUrl, 0.9),
    ];

    /// <inheritdoc />
    public Intent Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new Intent(IntentKind.Unknown, RawText: input, Confidence: 0.0);
        }

        var rawText = input;
        var normalized = Normalize(input);
        var command = TrimTrailingPoliteness(StripLeadingPoliteness(normalized));

        if (command.Length == 0)
        {
            return new Intent(IntentKind.Chat, RawText: rawText, Confidence: 0.0);
        }

        var lower = command.ToLowerInvariant();

        if (MediaPhrases.TryGetValue(lower, out var mediaCommand))
        {
            return new Intent(
                IntentKind.MediaControl,
                Media: mediaCommand,
                RawText: rawText,
                Confidence: 1.0);
        }

        var search = TrySearch(command, lower, rawText);
        if (search is not null)
        {
            return search;
        }

        var web = TryOpenWeb(command, lower, rawText);
        if (web is not null)
        {
            return web;
        }

        var app = TryLaunchApp(command, lower, rawText);
        if (app is not null)
        {
            return app;
        }

        // Non-automation fallback: the UI can answer helpfully without executing
        // anything, and unsupported path-like requests never reach automation.
        return new Intent(IntentKind.Chat, RawText: rawText, Confidence: 0.1);
    }

    private static Intent? TrySearch(string text, string lower, string rawText)
    {
        foreach (var spec in SearchSpecs)
        {
            if (!lower.StartsWith(spec.Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var query = text[spec.Prefix.Length..].Trim();
            if (query.Length == 0)
            {
                return null;
            }

            var candidate = spec.UrlPrefix + Uri.EscapeDataString(query);
            if (!TryNormalizeWebTarget(candidate, out var url))
            {
                return null;
            }

            return new Intent(
                IntentKind.OpenWeb,
                Target: url,
                RawText: rawText,
                Confidence: spec.Confidence);
        }

        return null;
    }

    private static Intent? TryOpenWeb(string text, string lower, string rawText)
    {
        foreach (var verb in NavigationVerbs)
        {
            if (!lower.StartsWith(verb, StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = text[verb.Length..].Trim();
            if (!TryNormalizeWebTarget(candidate, out var url))
            {
                return null;
            }

            return new Intent(
                IntentKind.OpenWeb,
                Target: url,
                RawText: rawText,
                Confidence: 1.0);
        }

        return null;
    }

    private static Intent? TryLaunchApp(string text, string lower, string rawText)
    {
        foreach (var verb in LaunchVerbs)
        {
            if (!lower.StartsWith(verb, StringComparison.Ordinal))
            {
                continue;
            }

            var alias = text[verb.Length..].Trim().TrimEnd('.', ',', '!', '?', ';');
            alias = NormalizeAppAlias(alias);

            if (alias is null || !AppAliases.Contains(alias))
            {
                return null;
            }

            return new Intent(
                IntentKind.LaunchApp,
                Target: alias,
                RawText: rawText,
                Confidence: 0.95);
        }

        return null;
    }

    private static string? NormalizeAppAlias(string alias)
    {
        if (alias.Length == 0
            || alias.Length > 64
            || alias.Any(char.IsControl)
            || alias.Contains('\\')
            || alias.Contains('/')
            || alias.Contains(':')
            || alias.Contains('%')
            || alias.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        if (alias.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            alias = alias[..^4].Trim();
        }

        return alias.ToLowerInvariant();
    }

    private static bool TryNormalizeWebTarget(string? candidate, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var value = candidate.Trim();

        if (value.Any(char.IsControl)
            || value.Any(char.IsWhiteSpace)
            || value.Contains('\\')
            || value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var hasExplicitScheme = value.Contains("://", StringComparison.Ordinal);
        if (!hasExplicitScheme)
        {
            if (!LooksLikeBareUrl(value))
            {
                return false;
            }

            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var scheme = uri.Scheme;
        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }

    private static bool LooksLikeBareUrl(string value)
    {
        if (value.Length > 2048 || value.Contains('@'))
        {
            return false;
        }

        var authorityEnd = value.IndexOfAny(['/', '?', '#']);
        var authority = authorityEnd < 0 ? value : value[..authorityEnd];

        if (authority.Length == 0
            || authority.StartsWith('.')
            || authority.StartsWith('-')
            || !authority.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in authority)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-'))
            {
                return false;
            }
        }

        if (value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var lastSegment = value;
        var lastSlash = value.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            lastSegment = value[(lastSlash + 1)..];
        }

        var queryStart = lastSegment.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            lastSegment = lastSegment[..queryStart];
        }

        var extension = Path.GetExtension(lastSegment);
        return !BlockedBareFileExtensions.Contains(extension);
    }

    private static string Normalize(string input)
    {
        var trimmed = input.Trim().TrimStart('.', ',', '!', '?', ';', ':');
        trimmed = trimmed.TrimEnd('.', ',', '!', '?', ';');

        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    private static string StripLeadingPoliteness(string text)
    {
        var current = text;

        for (var iteration = 0; iteration < 8 && current.Length > 0; iteration++)
        {
            var stripped = false;

            foreach (var prefix in ConversationalPrefixes)
            {
                if (!current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (current.Length == prefix.Length)
                {
                    return string.Empty;
                }

                var boundary = current[prefix.Length];
                if (boundary is not (' ' or ','))
                {
                    continue;
                }

                current = current[(prefix.Length + 1)..].TrimStart(' ', ',');
                stripped = true;
                break;
            }

            if (!stripped)
            {
                break;
            }
        }

        return current;
    }

    private static string TrimTrailingPoliteness(string text)
    {
        var current = text;

        for (var iteration = 0; iteration < 4 && current.Length > 0; iteration++)
        {
            var stripped = false;

            foreach (var suffix in ConversationalSuffixes)
            {
                if (current.Length <= suffix.Length
                    || !current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var boundary = current[current.Length - suffix.Length - 1];
                if (boundary is not (' ' or ','))
                {
                    continue;
                }

                current = current[..(current.Length - suffix.Length)].TrimEnd(' ', ',');
                stripped = true;
                break;
            }

            if (!stripped)
            {
                break;
            }
        }

        return current;
    }

    private readonly record struct SearchSpec(string Prefix, string UrlPrefix, double Confidence);
}
