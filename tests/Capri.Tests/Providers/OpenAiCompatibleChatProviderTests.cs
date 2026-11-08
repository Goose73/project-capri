using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Capri.Core.Models;
using Capri.Providers;
using Capri.Security;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Providers;

public sealed class OpenAiCompatibleChatProviderTests
{
    private const string BaseUrl = "http://provider.test/v1";
    private const string Model = "test-model";

    private const string CompletionJson = """
        {
          "id": "chatcmpl-1",
          "model": "test-model",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "Hello" },
              "finish_reason": "stop"
            }
          ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
        }
        """;

    [Fact]
    public async Task CompleteAsyncFormsRequestAndParsesResponse()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, apiKey: "test-key");

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Equal("Hello", result.Message.Content);
        Assert.Equal(ChatRole.Assistant, result.Message.Role);
        Assert.Equal("test-model", result.ModelId);
        Assert.Equal("stop", result.FinishReason);
        Assert.NotNull(result.Usage);
        Assert.Equal(10, result.Usage.PromptTokens);
        Assert.Equal(5, result.Usage.CompletionTokens);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("http://provider.test/v1/chat/completions", recorded.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", recorded.Authorization);
        Assert.Contains("application/json", recorded.Accept, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(recorded.Body!);
        var root = body.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(0.7, root.GetProperty("temperature").GetDouble());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("Hi", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsyncOmitsAuthorizationWhenCredentialIsEmpty()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(baseUrl: "http://localhost:11434/v1/"));

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        var recorded = Assert.Single(handler.Requests);
        Assert.Null(recorded.Authorization);
        Assert.Equal("http://localhost:11434/v1/chat/completions", recorded.RequestUri!.ToString());
    }

    [Fact]
    public async Task CompleteAsyncUsesRequestModelOverride()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        await provider.CompleteAsync(Request(model: "override-model"));

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("override-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task StreamAsyncParsesSseChunksAndStopsAtDone()
    {
        const string payload = """
            data: {"choices":[{"delta":{"role":"assistant"},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":"Hel"},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":"lo"},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            data: {"choices":[{"delta":{"content":"IGNORED"}}]}

            """;

        using var handler = FakeHttpMessageHandler.ServerSentEvents(payload);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var deltas = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Null(deltas.SingleOrDefault(delta => delta.Error is not null)?.Error);
        Assert.Equal("Hello", string.Concat(deltas.Select(delta => delta.Content)));
        Assert.Contains(deltas, delta => delta.Role == ChatRole.Assistant);
        Assert.Equal("stop", deltas[^1].FinishReason);
        Assert.DoesNotContain(deltas, delta => delta.Content.Contains("IGNORED", StringComparison.Ordinal));

        var recorded = Assert.Single(handler.Requests);
        Assert.Contains("text/event-stream", recorded.Accept, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(recorded.Body!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task StreamAsyncSkipsMalformedChunks()
    {
        const string payload = """
            data: this-is-not-json

            data: {"choices":[{"delta":{"content":"ok"},"finish_reason":null}]}

            data: [DONE]

            """;

        using var handler = FakeHttpMessageHandler.ServerSentEvents(payload);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var deltas = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal("ok", string.Concat(deltas.Select(delta => delta.Content)));
        Assert.All(deltas, delta => Assert.Null(delta.Error));
    }

    [Fact]
    public async Task StreamAsyncSurfacesEmbeddedErrorEnvelope()
    {
        const string payload = """
            data: {"error":{"message":"stream blew up","code":"bad_stream"}}

            data: [DONE]

            """;

        using var handler = FakeHttpMessageHandler.ServerSentEvents(payload);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var deltas = await CollectAsync(provider.StreamAsync(Request()));

        var delta = Assert.Single(deltas);
        Assert.NotNull(delta.Error);
        Assert.Equal(ProviderErrorKind.Unknown, delta.Error!.Kind);
        Assert.Equal("stream blew up", delta.Error.Message);
        Assert.Equal("bad_stream", delta.Error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorKind.Authentication, 1)]
    [InlineData(HttpStatusCode.BadRequest, ProviderErrorKind.InvalidRequest, 1)]
    [InlineData(HttpStatusCode.NotFound, ProviderErrorKind.NotFound, 1)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderErrorKind.RateLimited, 3)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderErrorKind.ServerError, 3)]
    public async Task CompleteAsyncMapsHttpStatusesAndRetriesOnlyTransientFailures(
        HttpStatusCode statusCode,
        ProviderErrorKind expectedKind,
        int expectedRequests)
    {
        using var handler = FakeHttpMessageHandler.FromFactory(() => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                """{"error":{"message":"provider says no"}}""",
                Encoding.UTF8,
                "application/json"),
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(expectedKind, result.Error!.Kind);
        Assert.Equal((int)statusCode, result.Error.StatusCode);
        Assert.Equal("provider says no", result.Error.Message);
        Assert.Equal(expectedRequests, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsyncPreservesErrorEnvelopeCode()
    {
        using var handler = FakeHttpMessageHandler.Json(
            """{"error":{"message":"bad model","code":"invalid_model"}}""",
            HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal("bad model", result.Error.Message);
        Assert.Equal("invalid_model", result.Error.Code);
    }

    [Fact]
    public async Task CompleteAsyncMapsTimeout()
    {
        using var handler = new FakeHttpMessageHandler(
            (_, _) => throw new TaskCanceledException("simulated timeout"));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.Timeout, result.Error!.Kind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsyncMapsNetworkFailureAfterLimitedRetries()
    {
        using var handler = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("simulated network failure"));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.Network, result.Error!.Kind);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsyncReportsConfigurationErrorWithoutSendingRequest()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(baseUrl: " "));

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.Configuration, result.Error!.Kind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task StreamAsyncSurfacesHttpErrorAsErrorDelta()
    {
        using var handler = FakeHttpMessageHandler.Json(
            """{"error":{"message":"bad key"}}""",
            HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var deltas = await CollectAsync(provider.StreamAsync(Request()));

        var delta = Assert.Single(deltas);
        Assert.Equal(string.Empty, delta.Content);
        Assert.NotNull(delta.Error);
        Assert.Equal(ProviderErrorKind.Authentication, delta.Error!.Kind);
        Assert.Equal("bad key", delta.Error.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task StreamAsyncReportsConfigurationErrorWithoutSendingRequest()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(baseUrl: string.Empty));

        var deltas = await CollectAsync(provider.StreamAsync(Request()));

        var delta = Assert.Single(deltas);
        Assert.NotNull(delta.Error);
        Assert.Equal(ProviderErrorKind.Configuration, delta.Error!.Kind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CheckHealthAsyncUsesModelsEndpointAndReportsLatency()
    {
        using var handler = FakeHttpMessageHandler.Json("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, apiKey: "health-key");

        var health = await provider.CheckHealthAsync();

        Assert.True(health.IsHealthy);
        Assert.NotNull(health.Latency);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("http://provider.test/v1/models", recorded.RequestUri!.ToString());
        Assert.Equal("Bearer health-key", recorded.Authorization);
    }

    [Fact]
    public async Task CheckHealthAsyncOmitsAuthorizationWithoutCredential()
    {
        using var handler = FakeHttpMessageHandler.Json("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.CheckHealthAsync();

        Assert.True(health.IsHealthy);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task CheckHealthAsyncMapsMissingModelsEndpoint()
    {
        using var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains("404", health.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealthAsyncMapsAuthenticationFailure()
    {
        using var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains("401", health.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealthAsyncMapsNetworkFailure()
    {
        using var handler = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("simulated network failure"));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var health = await provider.CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains("network", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static OpenAiCompatibleChatProvider CreateProvider(
        HttpClient client,
        AppSettings? settings = null,
        string? apiKey = null)
    {
        var credentialStore = new FakeCredentialStore();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            credentialStore
                .SetAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, apiKey)
                .GetAwaiter()
                .GetResult();
        }

        return new OpenAiCompatibleChatProvider(
            client,
            new FakeAppSettingsStore(settings ?? Settings()),
            credentialStore);
    }

    private static AppSettings Settings(
        string baseUrl = BaseUrl,
        string model = Model,
        bool streaming = true,
        int timeoutSeconds = 30)
    {
        var defaults = CapriDefaultSettings.Create();
        return defaults with
        {
            Provider = defaults.Provider with
            {
                BaseUrl = baseUrl,
                ChatModel = model,
                EnableStreaming = streaming,
                RequestTimeoutSeconds = timeoutSeconds,
            },
        };
    }

    private static ChatRequest Request(string content = "Hi", string? model = null)
    {
        return new ChatRequest([new ChatMessage(ChatRole.User, content)], ModelId: model);
    }

    private static async Task<List<ChatDelta>> CollectAsync(IAsyncEnumerable<ChatDelta> stream)
    {
        var deltas = new List<ChatDelta>();
        await foreach (var delta in stream)
        {
            deltas.Add(delta);
        }

        return deltas;
    }
}
