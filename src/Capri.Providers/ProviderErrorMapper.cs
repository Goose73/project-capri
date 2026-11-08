using System.Globalization;
using System.Net;
using System.Text.Json;
using Capri.Core.Models;

namespace Capri.Providers;

/// <summary>
/// Maps HTTP and JSON error envelopes to provider-neutral errors. Messages are
/// intentionally bounded and never include request headers or credentials.
/// </summary>
internal static class ProviderErrorMapper
{
    private const int MaxMessageLength = 512;
    private const int MaxCodeLength = 128;

    /// <summary>
    /// Maps an HTTP status code to a broad failure category.
    /// </summary>
    internal static ProviderErrorKind FromStatusCode(HttpStatusCode statusCode)
    {
        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
                return ProviderErrorKind.Authentication;
            case HttpStatusCode.Forbidden:
                return ProviderErrorKind.Authorization;
            case HttpStatusCode.NotFound:
                return ProviderErrorKind.NotFound;
            case HttpStatusCode.RequestTimeout:
                return ProviderErrorKind.Timeout;
            case HttpStatusCode.TooManyRequests:
                return ProviderErrorKind.RateLimited;
        }

        var numericStatus = (int)statusCode;
        if (numericStatus >= 500)
        {
            return ProviderErrorKind.ServerError;
        }

        return numericStatus >= 400
            ? ProviderErrorKind.InvalidRequest
            : ProviderErrorKind.Unknown;
    }

    /// <summary>
    /// Creates an error from a failed HTTP response.
    /// </summary>
    internal static ProviderError FromHttpResponse(
        HttpStatusCode statusCode,
        string? body,
        TimeSpan? retryAfter)
    {
        var envelope = ReadEnvelope(body);
        var message = envelope.Message ?? DefaultMessage(statusCode);

        if (statusCode == HttpStatusCode.TooManyRequests && retryAfter is { } delay && delay > TimeSpan.Zero)
        {
            message = FormattableString.Invariant(
                $"{message} Retry after {Math.Ceiling(delay.TotalSeconds):0} seconds.");
        }

        return new ProviderError(
            FromStatusCode(statusCode),
            Truncate(message, MaxMessageLength),
            TruncateCode(envelope.Code),
            (int)statusCode);
    }

    /// <summary>
    /// Creates an error from a JSON body that itself contains an error envelope
    /// (for example a 200 response carrying a provider error).
    /// </summary>
    internal static ProviderError FromEnvelope(
        string? body,
        ProviderErrorKind fallbackKind,
        int? statusCode)
    {
        var envelope = ReadEnvelope(body);
        return new ProviderError(
            fallbackKind,
            Truncate(envelope.Message ?? "The provider returned an error.", MaxMessageLength),
            TruncateCode(envelope.Code),
            statusCode);
    }

    private static string DefaultMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => "The provider rejected the request.",
            HttpStatusCode.Unauthorized => "Authentication failed.",
            HttpStatusCode.Forbidden => "Authorization failed.",
            HttpStatusCode.NotFound => "The requested model or endpoint was not found.",
            HttpStatusCode.RequestTimeout => "The provider request timed out.",
            HttpStatusCode.TooManyRequests => "The provider rate limited the request.",
            _ when (int)statusCode >= 500 => "The provider reported a server error.",
            _ => "The provider request failed.",
        };
    }

    private static (string? Message, string? Code) ReadEnvelope(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error))
            {
                return (null, null);
            }

            if (error.ValueKind == JsonValueKind.String)
            {
                return (error.GetString(), null);
            }

            if (error.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var message = GetString(error, "message");
            var code = GetString(error, "code") ?? GetString(error, "type");
            return (message, code);
        }
        catch (JsonException)
        {
            // A non-JSON error body is reported using the status-based default.
            return (null, null);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, maxLength), "...");
    }

    private static string? TruncateCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxCodeLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxCodeLength), "...");
    }
}
