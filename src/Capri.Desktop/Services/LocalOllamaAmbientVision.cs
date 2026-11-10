using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Capri.Desktop.Services;

public sealed record AmbientVisionObservation(bool Meaningful, string? Remark);

public interface IAmbientVisionClient
{
    Task<bool> IsModelReadyAsync(string model, CancellationToken cancellationToken);
    Task<AmbientVisionObservation> ObserveAsync(
        string model, byte[] jpeg, CancellationToken cancellationToken);
}

/// <summary>
/// Sends only user-consented transient webcam JPEGs to the fixed Ollama
/// loopback address. No configured provider URL, DNS, proxies or cloud fallback.
/// </summary>
public sealed class LocalOllamaAmbientVision : IAmbientVisionClient, IDisposable
{
    private const int MaxResponseBytes = 64 * 1024;
    private const int MaxFrameBytes = 450_000;
    private static readonly Uri LocalBase = new("http://127.0.0.1:11434/");
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public LocalOllamaAmbientVision() : this(new HttpClient(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
    })
    {
        Timeout = TimeSpan.FromSeconds(25)
    }, true)
    {
    }

    internal LocalOllamaAmbientVision(HttpClient client, bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
    }

    public static bool IsValidModel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return false;
        return name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/');
    }

    public async Task<bool> IsModelReadyAsync(string model, CancellationToken cancellationToken)
    {
        if (!IsValidModel(model))
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(LocalBase, "api/tags"));
        using var response = await SendLocalAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return false;
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var entry in models.EnumerateArray())
        {
            if (entry.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), model, StringComparison.OrdinalIgnoreCase))
                return true;
            if (entry.TryGetProperty("model", out var id) &&
                string.Equals(id.GetString(), model, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task<AmbientVisionObservation> ObserveAsync(
        string model, byte[] jpeg, CancellationToken cancellationToken)
    {
        if (!IsValidModel(model))
            throw new ArgumentException("Invalid local vision model identifier.", nameof(model));
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length is < 128 or > MaxFrameBytes ||
            jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new InvalidDataException("Ambient camera frame is not a bounded JPEG.");

        var payload = JsonSerializer.Serialize(new
        {
            model,
            stream = false,
            format = "json",
            options = new { temperature = 0.2, num_predict = 160 },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content =
                        "You are Capri, an optional friendly desktop companion. This is a single webcam snapshot, " +
                        "NOT a view of the user's screen, game or desktop. Describe no identifying, private, " +
                        "sensitive, demographic, medical, emotional or inferred personal characteristics. " +
                        "Do not claim to know what the user is thinking or doing beyond visible objects. " +
                        "Only if there is one new, benign, notable visible activity or object suitable for a " +
                        "brief optional companion comment, set meaningful true and write one casual, polite " +
                        "sentence under 140 characters in remark. Do not repeat generic remarks, comment on " +
                        "appearances, or mention surveillance. Otherwise return meaningful false and an empty " +
                        "remark. Output only JSON with keys meaningful (boolean) and remark (string).",
                    images = new[] { Convert.ToBase64String(jpeg) }
                }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(LocalBase, "api/chat"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await SendLocalAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var text) ||
            text.ValueKind != JsonValueKind.String)
            return new AmbientVisionObservation(false, null);

        var modelOutput = text.GetString();
        if (modelOutput is null || modelOutput.Length > 2048)
            return new AmbientVisionObservation(false, null);
        try
        {
            using var observation = JsonDocument.Parse(modelOutput);
            var content = observation.RootElement;
            if (!content.TryGetProperty("meaningful", out var meaningful) ||
                meaningful.ValueKind != JsonValueKind.True ||
                !content.TryGetProperty("remark", out var remark) ||
                remark.ValueKind != JsonValueKind.String)
                return new AmbientVisionObservation(false, null);
            var candidate = remark.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 140 ||
                candidate.Any(char.IsControl))
                return new AmbientVisionObservation(false, null);
            return new AmbientVisionObservation(true, candidate);
        }
        catch (JsonException)
        {
            return new AmbientVisionObservation(false, null);
        }
    }

    private async Task<HttpResponseMessage> SendLocalAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Validate even injected test requests. This class has no user supplied URL.
        if (request.RequestUri?.Scheme != Uri.UriSchemeHttp ||
            request.RequestUri.Host != "127.0.0.1" ||
            request.RequestUri.Port != 11434)
            throw new InvalidOperationException("Ambient vision requires local Ollama loopback.");
        return await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (data.Length + count > MaxResponseBytes)
                throw new InvalidDataException("The local vision response is too large.");
            data.Write(buffer, 0, count);
        }
        data.Position = 0;
        return await JsonDocument.ParseAsync(data, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
