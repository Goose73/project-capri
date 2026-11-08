using System.Text.Json.Serialization;

namespace Capri.Cli.Services;

/// <summary>Model information returned by the local Ollama API.</summary>
public sealed record OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("modified_at")]
    public string? ModifiedAt { get; init; }

    [JsonPropertyName("size")]
    public long? Size { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("details")]
    public OllamaModelDetails? Details { get; init; }
}

/// <summary>Optional detail fields reported by Ollama for an installed model.</summary>
public sealed record OllamaModelDetails
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("family")]
    public string? Family { get; init; }

    [JsonPropertyName("families")]
    public IReadOnlyList<string>? Families { get; init; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; init; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; init; }
}

/// <summary>Progress/status update from an Ollama pull operation.</summary>
public sealed record OllamaPullProgress(
    string Status,
    string? Digest = null,
    long? Total = null,
    long? Completed = null,
    string? Error = null);

/// <summary>
/// Abstraction over the local Ollama HTTP API. Implementations must not
/// download models without an explicit caller request.
/// </summary>
public interface IOllamaClient
{
    /// <summary>Lists models installed in the local Ollama instance.</summary>
    Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(
        string baseUrl,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true when the named model is available locally.</summary>
    Task<bool> ModelExistsAsync(
        string baseUrl,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>Streams pull progress for a model. Caller must have obtained explicit confirmation.</summary>
    IAsyncEnumerable<OllamaPullProgress> PullModelAsync(
        string baseUrl,
        string modelName,
        CancellationToken cancellationToken = default);
}
