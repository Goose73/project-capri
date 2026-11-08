using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Capri.Providers;

/// <summary>
/// Chat provider for OpenAI-compatible HTTP APIs, including OpenAI, Ollama,
/// and LM Studio. Streaming uses standard SSE <c>data:</c> frames and tolerates
/// providers that omit optional fields.
/// </summary>
public sealed partial class OpenAiCompatibleChatProvider : IChatProvider
{
    /// <summary>Named <see cref="HttpClient"/> registered by the DI extension.</summary>
    public const string HttpClientName = "capri.openai-compatible";

    /// <summary>
    /// Legacy credential-store key containing a shared provider API token.
    /// Retained for compatibility. Provider requests use isolated per-profile
    /// keys computed by <see cref="ProviderCredentials"/>; the legacy key is
    /// only read for the one-time OpenAI migration.
    /// </summary>
    public const string ApiKeyCredentialKey = ProviderCredentials.LegacyApiKeyCredentialKey;

    private const string DefaultProviderId = "openai";
    private const string ChatCompletionsPath = "chat/completions";
    private const string ModelsPath = "models";
    private const int DefaultRequestTimeoutSeconds = 60;
    private const int MaxRetries = 2;
    private const int RetryBaseDelayMilliseconds = 100;

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly IAppSettingsStore _settings;
    private readonly ISecureCredentialStore _credentials;
    private readonly ILogger<OpenAiCompatibleChatProvider> _logger;

    /// <summary>
    /// Creates a provider over an injected <see cref="HttpClient"/>.
    /// </summary>
    public OpenAiCompatibleChatProvider(
        HttpClient httpClient,
        IAppSettingsStore settings,
        ISecureCredentialStore credentials,
        ILogger<OpenAiCompatibleChatProvider>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _logger = logger ?? NullLogger<OpenAiCompatibleChatProvider>.Instance;
    }

    /// <inheritdoc />
    public string ProviderId => ResolveProviderId(_settings.Current.Provider.ProviderId);

    /// <inheritdoc />
    public async Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveRequest(request, out var provider, out var model, out var endpoint, out var resolutionError))
        {
            return Failure(resolutionError!);
        }

        var guardDecision = EvaluateGuard(provider, model);
        if (!guardDecision.IsAllowed)
        {
            return Failure(guardDecision.ToError());
        }

        string? apiKey;
        try
        {
            apiKey = await GetApiKeyAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCredentialReadFailure(exception))
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Configuration,
                "The provider credential could not be read."));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetRequestTimeout(provider));

        try
        {
            using var response = await SendWithRetryAsync(
                () => CreateCompletionRequest(
                    endpoint,
                    apiKey,
                    BuildPayload(request, model, provider, stream: false),
                    stream: false),
                timeoutCts.Token).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Failure(CreateHttpError(response, body));
            }

            return ParseCompletion(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(new ProviderError(ProviderErrorKind.Timeout, "The provider request timed out."));
        }
        catch (HttpRequestException)
        {
            return Failure(new ProviderError(ProviderErrorKind.Network, "Could not reach the provider."));
        }
        catch (IOException)
        {
            return Failure(new ProviderError(ProviderErrorKind.Network, "The provider connection failed."));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveRequest(request, out var provider, out var model, out var endpoint, out var resolutionError))
        {
            yield return ErrorDelta(resolutionError!);
            yield break;
        }

        var guardDecision = EvaluateGuard(provider, model);
        if (!guardDecision.IsAllowed)
        {
            yield return ErrorDelta(guardDecision.ToError());
            yield break;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetRequestTimeout(provider));
        var requestToken = timeoutCts.Token;

        string? apiKey = null;
        ProviderError? credentialError = null;
        try
        {
            apiKey = await GetApiKeyAsync(provider, requestToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            credentialError = new ProviderError(ProviderErrorKind.Timeout, "The provider request timed out.");
        }
        catch (Exception exception) when (IsCredentialReadFailure(exception))
        {
            credentialError = new ProviderError(
                ProviderErrorKind.Configuration,
                "The provider credential could not be read.");
        }

        if (credentialError is not null)
        {
            yield return ErrorDelta(credentialError);
            yield break;
        }

        HttpResponseMessage? response = null;
        ProviderError? sendError = null;
        try
        {
            // Retries are bounded by SendWithRetryAsync and the overall request timeout.
            response = await SendWithRetryAsync(
                () => CreateCompletionRequest(
                    endpoint,
                    apiKey,
                    BuildPayload(request, model, provider, stream: true),
                    stream: true),
                requestToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sendError = new ProviderError(ProviderErrorKind.Timeout, "The provider request timed out.");
        }
        catch (HttpRequestException)
        {
            sendError = new ProviderError(ProviderErrorKind.Network, "Could not reach the provider.");
        }
        catch (IOException)
        {
            sendError = new ProviderError(ProviderErrorKind.Network, "The provider connection failed.");
        }

        if (sendError is not null || response is null)
        {
            yield return ErrorDelta(sendError ?? new ProviderError(
                ProviderErrorKind.Unknown,
                "The provider response was not available."));
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string? body = null;
                ProviderError? bodyError = null;
                try
                {
                    body = await response.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    bodyError = new ProviderError(ProviderErrorKind.Timeout, "The provider request timed out.");
                }
                catch (IOException)
                {
                    bodyError = new ProviderError(ProviderErrorKind.Network, "The provider connection failed.");
                }
                catch (HttpRequestException)
                {
                    bodyError = new ProviderError(ProviderErrorKind.Network, "Could not reach the provider.");
                }

                if (bodyError is not null)
                {
                    yield return ErrorDelta(bodyError);
                    yield break;
                }

                yield return ErrorDelta(CreateHttpError(response, body));
                yield break;
            }

            Stream? responseStream = null;
            ProviderError? streamError = null;
            try
            {
                responseStream = await response.Content
                    .ReadAsStreamAsync(requestToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                streamError = new ProviderError(ProviderErrorKind.Timeout, "The provider request timed out.");
            }
            catch (HttpRequestException)
            {
                streamError = new ProviderError(ProviderErrorKind.Network, "Could not reach the provider.");
            }
            catch (IOException)
            {
                streamError = new ProviderError(ProviderErrorKind.Network, "The provider connection failed.");
            }

            if (responseStream is null)
            {
                yield return ErrorDelta(streamError ?? new ProviderError(
                    ProviderErrorKind.Unknown,
                    "The provider response could not be read."));
                yield break;
            }

            await using (responseStream)
            {
                var parser = SseParser.Create(responseStream);
                await using var enumerator = parser
                    .EnumerateAsync(requestToken)
                    .GetAsyncEnumerator(requestToken);

                while (true)
                {
                    var moved = false;
                    ProviderError? readError = null;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        readError = new ProviderError(ProviderErrorKind.Timeout, "The provider stream timed out.");
                    }
                    catch (HttpRequestException)
                    {
                        readError = new ProviderError(ProviderErrorKind.Network, "Could not reach the provider.");
                    }
                    catch (IOException)
                    {
                        readError = new ProviderError(
                            ProviderErrorKind.Network,
                            "The provider connection was interrupted.");
                    }

                    if (readError is not null)
                    {
                        yield return ErrorDelta(readError);
                        yield break;
                    }

                    if (!moved)
                    {
                        yield break;
                    }

                    var data = enumerator.Current.Data;
                    if (string.IsNullOrWhiteSpace(data))
                    {
                        continue;
                    }

                    if (string.Equals(data.Trim(), "[DONE]", StringComparison.Ordinal))
                    {
                        yield break;
                    }

                    ChatDelta? delta = null;
                    ProviderError? parseError = null;
                    try
                    {
                        delta = ParseStreamChunk(data, out parseError);
                    }
                    catch (JsonException)
                    {
                        LogMalformedStreamingChunk(_logger);
                        continue;
                    }

                    if (parseError is not null)
                    {
                        yield return ErrorDelta(parseError);
                        yield break;
                    }

                    if (delta is not null)
                    {
                        yield return delta;
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = _settings.Current.Provider;

        var profile = FreeModelGuard.ResolveProfile(provider);
        var guardDecision = FreeModelGuard.EvaluateHealth(provider, profile);
        if (!guardDecision.IsAllowed)
        {
            return new ProviderHealth(false, guardDecision.Message);
        }

        if (!TryBuildEndpoint(provider.BaseUrl, ModelsPath, out var endpoint, out var configurationError))
        {
            return new ProviderHealth(false, configurationError!.Message);
        }

        string? apiKey;
        try
        {
            apiKey = await GetApiKeyAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCredentialReadFailure(exception))
        {
            return new ProviderHealth(false, "The provider credential could not be read.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetHealthTimeout(provider));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await SendWithRetryAsync(
                () => CreateHealthRequest(endpoint, apiKey),
                timeoutCts.Token).ConfigureAwait(false);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealth(true, "Provider is reachable.", stopwatch.Elapsed);
            }

            var message = response.StatusCode switch
            {
                HttpStatusCode.NotFound => "Provider health endpoint was not found (404).",
                HttpStatusCode.Unauthorized => "Provider authentication failed (401).",
                HttpStatusCode.Forbidden => "Provider authorization failed (403).",
                HttpStatusCode.TooManyRequests => "Provider rate limited the health probe (429).",
                _ when (int)response.StatusCode >= 500 => "Provider reported a server error.",
                _ => "Provider health probe failed.",
            };

            return new ProviderHealth(false, message, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderHealth(false, "Provider health probe timed out.", stopwatch.Elapsed);
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            return new ProviderHealth(false, "Provider is unreachable (network error).", stopwatch.Elapsed);
        }
        catch (IOException)
        {
            stopwatch.Stop();
            return new ProviderHealth(false, "Provider connection failed.", stopwatch.Elapsed);
        }
    }

    private static FreeModelGuardDecision EvaluateGuard(
        AppSettings.ProviderSettings provider,
        string? modelId)
    {
        var profile = FreeModelGuard.ResolveProfile(provider);
        return FreeModelGuard.Evaluate(provider, profile, modelId);
    }

    private async Task<string?> GetApiKeyAsync(
        AppSettings.ProviderSettings provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var providerId = ResolveProviderId(provider.ProviderId);

        if (!ProviderCatalog.TryGetById(providerId, out var profile))
        {
            // Unknown provider identifiers are isolated from the legacy key.
            // Loopback endpoints fail closed without reading any credential.
            if (ProviderCatalog.IsLoopbackAllowedHttp(provider.BaseUrl))
            {
                return null;
            }

            var isolatedKey = ProviderCredentials.GetApiKeyCredentialKey(providerId);
            var isolatedValue = await _credentials
                .GetAsync(isolatedKey, cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(isolatedValue) ? null : isolatedValue.Trim();
        }

        // Local or keyless profiles never read credentials and never send an
        // Authorization header, even when credentials exist in the store.
        if (!profile.RequiresApiKey || profile.IsLocal)
        {
            return null;
        }

        var credentialKey = ProviderCredentials.GetApiKeyCredentialKey(profile);
        var allowLegacyFallback = string.Equals(
            profile.Id,
            ProviderCatalog.OpenAiId,
            StringComparison.OrdinalIgnoreCase);

        return await CredentialMigration.ResolveApiKeyAsync(
            _credentials,
            credentialKey,
            allowLegacyFallback,
            cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveProviderId(string? providerId)
    {
        return string.IsNullOrWhiteSpace(providerId)
            ? DefaultProviderId
            : providerId.Trim();
    }

    private bool TryResolveRequest(
        ChatRequest request,
        out AppSettings.ProviderSettings provider,
        out string model,
        out Uri endpoint,
        out ProviderError? error)
    {
        provider = _settings.Current.Provider ?? new AppSettings.ProviderSettings();
        model = string.Empty;
        endpoint = null!;

        if (request.Messages is null || request.Messages.Count == 0)
        {
            error = new ProviderError(
                ProviderErrorKind.InvalidRequest,
                "At least one chat message is required.");
            return false;
        }

        var requestedModel = string.IsNullOrWhiteSpace(request.ModelId)
            ? provider.ChatModel
            : request.ModelId;

        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            error = new ProviderError(
                ProviderErrorKind.Configuration,
                "No chat model is configured.");
            return false;
        }

        model = requestedModel.Trim();
        return TryBuildEndpoint(provider.BaseUrl, ChatCompletionsPath, out endpoint, out error);
    }

    private static bool TryBuildEndpoint(
        string? baseUrl,
        string relativePath,
        out Uri endpoint,
        out ProviderError? error)
    {
        endpoint = null!;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = new ProviderError(
                ProviderErrorKind.Configuration,
                "No provider base URL is configured.");
            return false;
        }

        var normalized = baseUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/chat/completions".Length];
        }

        var candidate = string.Concat(normalized, "/", relativePath);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            error = new ProviderError(
                ProviderErrorKind.Configuration,
                "The provider base URL is not a valid HTTP or HTTPS address.");
            return false;
        }

        endpoint = uri;
        error = null;
        return true;
    }

    private static string BuildPayload(
        ChatRequest request,
        string model,
        AppSettings.ProviderSettings provider,
        bool stream)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            messages.Add(new JsonObject
            {
                ["role"] = ToRoleString(message.Role),
                ["content"] = message.Content ?? string.Empty,
            });
        }

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = stream,
        };

        var temperature = request.Temperature ?? provider.Temperature;
        if (double.IsFinite(temperature))
        {
            payload["temperature"] = temperature;
        }

        var maxOutputTokens = request.MaxOutputTokens ?? provider.MaxOutputTokens;
        if (maxOutputTokens > 0)
        {
            payload["max_tokens"] = maxOutputTokens;
        }

        // OpenAI-compatible JSON mode hint. This is opt-in so existing request
        // shapes (and providers that do not understand response_format) are
        // unaffected. No credentials or request content are added here.
        if (request.RequireJsonObject)
        {
            payload["response_format"] = new JsonObject
            {
                ["type"] = "json_object",
            };
        }

        return payload.ToJsonString();
    }

    private static HttpRequestMessage CreateCompletionRequest(
        Uri endpoint,
        string? apiKey,
        string payload,
        bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        ApplyAuthentication(request, apiKey);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));

        return request;
    }

    private static HttpRequestMessage CreateHealthRequest(Uri endpoint, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        ApplyAuthentication(request, apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static void ApplyAuthentication(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = requestFactory();
                var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!IsRetryableStatus(response.StatusCode) || attempt >= MaxRetries)
                {
                    return response;
                }

                var retryDelay = GetRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(GetBackoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > MaxRetryDelay ? MaxRetryDelay : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait > MaxRetryDelay ? MaxRetryDelay : wait;
            }
        }

        return GetBackoff(attempt);
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    private static TimeSpan GetBackoff(int attempt)
    {
        return TimeSpan.FromMilliseconds(RetryBaseDelayMilliseconds * (1 << attempt));
    }

    private static ProviderError CreateHttpError(HttpResponseMessage response, string? body)
    {
        return ProviderErrorMapper.FromHttpResponse(response.StatusCode, body, GetRetryAfter(response));
    }

    private static ChatCompletion ParseCompletion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.Unknown,
                    "Provider returned an unexpected response shape."));
            }

            if (root.TryGetProperty("error", out _))
            {
                return Failure(ProviderErrorMapper.FromEnvelope(
                    root.GetRawText(),
                    ProviderErrorKind.Unknown,
                    statusCode: null));
            }

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return Failure(new ProviderError(
                    ProviderErrorKind.Unknown,
                    "Provider response did not contain any choices."));
            }

            var choice = choices[0];
            var message = choice.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.Object
                    ? messageElement
                    : default;

            var content = GetString(message, "content") ?? string.Empty;
            var finishReason = GetString(choice, "finish_reason");
            var modelId = GetString(root, "model");
            var usage = ParseUsage(root);

            return new ChatCompletion(
                new ChatMessage(ChatRole.Assistant, content),
                modelId,
                finishReason,
                usage);
        }
        catch (JsonException)
        {
            return Failure(new ProviderError(
                ProviderErrorKind.Unknown,
                "Provider returned a malformed response."));
        }
    }

    private static ChatDelta? ParseStreamChunk(string data, out ProviderError? error)
    {
        error = null;

        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("error", out _))
        {
            error = ProviderErrorMapper.FromEnvelope(
                root.GetRawText(),
                ProviderErrorKind.Unknown,
                statusCode: null);
            return null;
        }

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choices[0];
        var payload = choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
            ? delta
            : choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                ? message
                : default;

        var content = GetString(payload, "content") ?? string.Empty;
        var role = ParseRole(GetString(payload, "role"));
        var finishReason = GetString(choice, "finish_reason");

        if (content.Length == 0 && role is null && finishReason is null)
        {
            return null;
        }

        return new ChatDelta(content, role, finishReason);
    }

    private static TokenUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var promptTokens = GetInt32(usage, "prompt_tokens");
        var completionTokens = GetInt32(usage, "completion_tokens");

        return promptTokens.HasValue && completionTokens.HasValue
            ? new TokenUsage(promptTokens.Value, completionTokens.Value)
            : null;
    }

    private static string ToRoleString(ChatRole role)
    {
        return role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user",
        };
    }

    private static ChatRole? ParseRole(string? role)
    {
        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
        {
            return ChatRole.System;
        }

        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return ChatRole.User;
        }

        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            return ChatRole.Assistant;
        }

        if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return ChatRole.Tool;
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static ChatCompletion Failure(ProviderError error)
    {
        return new ChatCompletion(
            new ChatMessage(ChatRole.Assistant, string.Empty),
            Error: error);
    }

    private static ChatDelta ErrorDelta(ProviderError error)
    {
        return new ChatDelta(string.Empty, Error: error);
    }

    private static TimeSpan GetRequestTimeout(AppSettings.ProviderSettings provider)
    {
        var seconds = provider.RequestTimeoutSeconds > 0
            ? provider.RequestTimeoutSeconds
            : DefaultRequestTimeoutSeconds;

        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan GetHealthTimeout(AppSettings.ProviderSettings provider)
    {
        var seconds = provider.RequestTimeoutSeconds > 0
            ? Math.Min(provider.RequestTimeoutSeconds, 15)
            : 10;

        return TimeSpan.FromSeconds(Math.Max(seconds, 5));
    }

    private static bool IsCredentialReadFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or CryptographicException;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Skipping malformed streaming chunk.")]
    private static partial void LogMalformedStreamingChunk(ILogger logger);
}
