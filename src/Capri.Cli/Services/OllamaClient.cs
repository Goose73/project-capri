using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capri.Cli.Services;

/// <summary>
/// HTTP client for the local Ollama native API (<c>/api/*</c>).
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    /// <summary>Creates a client using the provided <see cref="HttpClient"/>.</summary>
    public OllamaClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Derives the Ollama native API root from a configured base URL. OpenAI-
    /// compatible settings often use <c>/v1</c>; native endpoints live at <c>/api</c>.
    /// </summary>
    public static string GetApiRoot(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "http://127.0.0.1:11434";
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].TrimEnd('/');
        }

        if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4].TrimEnd('/');
        }

        return trimmed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetApiRoot(baseUrl);

        foreach (var path in new[] { "/api/tags", "/v1/models" })
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync($"{root}{path}", cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                var payload = await JsonSerializer
                    .DeserializeAsync<TagsResponse>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (payload?.Models is { Count: > 0 } models)
                {
                    return models;
                }

                // /v1/models may return {data:[{id:...}]} shape; accept empty as valid and try to parse below.
                if (path == "/api/tags")
                {
                    return payload?.Models ?? [];
                }
            }
            catch (HttpRequestException) when (path == "/api/tags")
            {
            }
            catch (JsonException) when (path == "/api/tags")
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && path == "/api/tags")
            {
            }
        }

        // Fallback: try /v1/models OpenAI shape and map to OllamaModelInfo.
        try
        {
            using var response = await _httpClient.GetAsync($"{root}/v1/models", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                var list = new List<OllamaModelInfo>();
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        var name = id.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            list.Add(new OllamaModelInfo { Name = name, Model = name });
                        }
                    }
                }

                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
        }

        using var finalResponse = await _httpClient.GetAsync($"{root}/api/tags", cancellationToken).ConfigureAwait(false);
        finalResponse.EnsureSuccessStatusCode();
        await using var finalStream = await finalResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var finalPayload = await JsonSerializer.DeserializeAsync<TagsResponse>(finalStream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return finalPayload?.Models ?? [];
    }

    /// <inheritdoc />
    public async Task<bool> ModelExistsAsync(
        string baseUrl,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var root = GetApiRoot(baseUrl);
            using var content = JsonContent.Create(new { name = modelName.Trim() }, options: JsonOptions);
            using var response = await _httpClient
                .PostAsync($"{root}/api/show", content, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            var models = await ListModelsAsync(baseUrl, cancellationToken).ConfigureAwait(false);
            var needle = modelName.Trim();
            return models.Any(model =>
                string.Equals(model.Name, needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(model.Model, needle, StringComparison.OrdinalIgnoreCase));
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<OllamaPullProgress> PullModelAsync(
        string baseUrl,
        string modelName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        cancellationToken.ThrowIfCancellationRequested();

        var root = GetApiRoot(baseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{root}/api/pull")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { name = modelName.Trim(), stream = true }, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            var message = TryExtractError(body) ?? $"Ollama pull failed (HTTP {(int)response.StatusCode}).";
            yield return new OllamaPullProgress("error", Error: message);
            yield break;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            PullResponse? item = null;
            try
            {
                item = JsonSerializer.Deserialize<PullResponse>(line, JsonOptions);
            }
            catch (JsonException)
            {
            }

            if (item is null)
            {
                continue;
            }

            yield return new OllamaPullProgress(
                item.Status ?? string.Empty,
                item.Digest,
                item.Total,
                item.Completed,
                item.Error);

            if (!string.IsNullOrWhiteSpace(item.Error)
                || string.Equals(item.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
        }
    }

    private static async Task<string?> SafeReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 200 ? body[..200] : body;
    }

    private sealed class TagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelInfo>? Models { get; init; }
    }

    private sealed class PullResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }

        [JsonPropertyName("total")]
        public long? Total { get; init; }

        [JsonPropertyName("completed")]
        public long? Completed { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
