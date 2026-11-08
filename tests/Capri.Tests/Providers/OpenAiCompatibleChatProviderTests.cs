using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Capri.Core.Abstractions;
using Capri.Core.Models;
using Capri.Core.Providers;
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
    public async Task CompleteAsyncIncludesJsonResponseFormatWhenRequested()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, apiKey: "test-key");

        var result = await provider.CompleteAsync(Request(requireJsonObject: true));

        Assert.Null(result.Error);
        var recorded = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(recorded.Body!);
        var root = body.RootElement;

        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        // Existing request fields are preserved and no credential leaks into the body.
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.DoesNotContain("test-key", recorded.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsyncOmitsJsonResponseFormatByDefault()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
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

    [Theory]
    [InlineData(ProviderCatalog.OpenAiId)]
    [InlineData(ProviderCatalog.OpenRouterId)]
    public async Task CloudProfileWithoutConsentBlocksChatWithoutSendingRequestOrReadingCredentials(
        string providerId)
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(ProviderCredentials.GetApiKeyCredentialKey(providerId), "profile-key");
        var provider = CreateProvider(
            client,
            Settings(providerId: providerId, allowCloudInference: false),
            store);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.Configuration, result.Error!.Kind);
        Assert.Equal(FreeModelGuard.CloudInferenceDisabledMessage, result.Error.Message);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(store.Gets);
    }

    [Fact]
    public async Task CloudProfileWithoutConsentBlocksStreamWithoutSendingRequest()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(
            client,
            Settings(providerId: ProviderCatalog.OpenRouterId, allowCloudInference: false));

        var deltas = await CollectAsync(provider.StreamAsync(Request()));

        var delta = Assert.Single(deltas);
        Assert.NotNull(delta.Error);
        Assert.Equal(ProviderErrorKind.Configuration, delta.Error!.Kind);
        Assert.Equal(FreeModelGuard.CloudInferenceDisabledMessage, delta.Error.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CloudHealthProbeWithoutConsentDoesNotContactProvider()
    {
        using var handler = FakeHttpMessageHandler.Json("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var provider = CreateProvider(
            client,
            Settings(providerId: ProviderCatalog.OpenAiId, allowCloudInference: false));

        var health = await provider.CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Equal(FreeModelGuard.CloudInferenceDisabledMessage, health.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task LocalProfileCompletesWithoutCloudConsent()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(
            ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OllamaId),
            "ollama-key");
        var provider = CreateProvider(
            client,
            Settings(
                baseUrl: "http://localhost:11434/v1",
                providerId: ProviderCatalog.OllamaId,
                freeModelsOnly: true,
                allowCloudInference: false),
            store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Equal("Hello", result.Message.Content);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
        Assert.Empty(store.Gets);
    }

    [Fact]
    public async Task OpenRouterFreeModelIsAllowedWithConsentAndFreeOnly()
    {
        const string freeModel = "meta-llama/llama-3.1-8b-instruct:free";
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(
            ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenRouterId),
            "router-key");
        var provider = CreateProvider(
            client,
            Settings(
                providerId: ProviderCatalog.OpenRouterId,
                model: freeModel,
                freeModelsOnly: true,
                allowCloudInference: true),
            store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("Bearer router-key", recorded.Authorization);
        using var body = JsonDocument.Parse(recorded.Body!);
        Assert.Equal(freeModel, body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task OpenRouterNonFreeModelIsBlockedWhenFreeOnly()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(
            ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenRouterId),
            "router-key");
        var provider = CreateProvider(
            client,
            Settings(
                providerId: ProviderCatalog.OpenRouterId,
                model: "openai/gpt-4o",
                freeModelsOnly: true,
                allowCloudInference: true),
            store);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(FreeModelGuard.FreeModelBlockedMessage, result.Error.Message);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(store.Gets);
    }

    [Fact]
    public async Task CloudFreeOnlyWithoutModelFailsClosedWithoutSendingRequest()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(
            client,
            Settings(
                model: string.Empty,
                providerId: ProviderCatalog.OpenAiId,
                freeModelsOnly: true,
                allowCloudInference: true));

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

    [Theory]
    [InlineData("ollama", "http://localhost:11434/v1")]
    [InlineData("lmstudio", "http://127.0.0.1:1234/v1")]
    public async Task LocalProfilesNeverReadCredentialsOrSendAuthorizationForChat(
        string providerId,
        string baseUrl)
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");
        await store.SeedAsync(ProviderCredentials.GetApiKeyCredentialKey(providerId), "profile-key");
        var provider = CreateProvider(
            client,
            Settings(baseUrl: baseUrl, providerId: providerId),
            store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Equal("Hello", result.Message.Content);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
        Assert.Empty(store.Gets);
        Assert.Empty(store.HasChecks);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
    }

    [Fact]
    public async Task LocalProfileHealthAndStreamNeverReadCredentialsOrSendAuthorization()
    {
        const string providerId = ProviderCatalog.OllamaId;
        const string baseUrl = "http://localhost:11434/v1";
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");
        await store.SeedAsync(ProviderCredentials.GetApiKeyCredentialKey(providerId), "profile-key");

        using (var healthHandler = FakeHttpMessageHandler.Json("""{"data":[]}"""))
        using (var healthClient = new HttpClient(healthHandler))
        {
            var provider = CreateProvider(
                healthClient,
                Settings(baseUrl: baseUrl, providerId: providerId),
                store);

            var health = await provider.CheckHealthAsync();

            Assert.True(health.IsHealthy);
            Assert.Null(Assert.Single(healthHandler.Requests).Authorization);
        }

        const string payload = """
            data: {"choices":[{"delta":{"content":"ok"},"finish_reason":null}]}

            data: [DONE]

            """;

        using (var streamHandler = FakeHttpMessageHandler.ServerSentEvents(payload))
        using (var streamClient = new HttpClient(streamHandler))
        {
            var provider = CreateProvider(
                streamClient,
                Settings(baseUrl: baseUrl, providerId: providerId),
                store);

            var deltas = await CollectAsync(provider.StreamAsync(Request()));

            Assert.Equal("ok", string.Concat(deltas.Select(delta => delta.Content)));
            Assert.Null(Assert.Single(streamHandler.Requests).Authorization);
        }

        Assert.Empty(store.Gets);
        Assert.Empty(store.HasChecks);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
    }

    [Fact]
    public async Task LocalProfileWithoutStoredCredentialStillCompletes()
    {
        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        var provider = CreateProvider(
            client,
            Settings(baseUrl: "http://localhost:11434/v1", providerId: ProviderCatalog.OllamaId),
            store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
        Assert.Empty(store.Gets);
        Assert.Empty(store.HasChecks);
    }

    [Fact]
    public async Task OpenAiUsesProfileCredentialAndDoesNotTouchLegacyKey()
    {
        var profileKey = ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");
        await store.SeedAsync(profileKey, "openai-key");

        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(providerId: ProviderCatalog.OpenAiId), store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Equal("Bearer openai-key", Assert.Single(handler.Requests).Authorization);
        Assert.DoesNotContain(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, store.Gets);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
        Assert.Equal("legacy-key", await store.PeekAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey));
    }

    [Fact]
    public async Task OpenAiMigratesLegacyCredentialOnceThenUsesMigratedKey()
    {
        var profileKey = ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenAi);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");

        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(providerId: ProviderCatalog.OpenAiId), store);

        var first = await provider.CompleteAsync(Request());

        Assert.Null(first.Error);
        Assert.Equal("Bearer legacy-key", handler.Requests[0].Authorization);
        Assert.Equal(new[] { profileKey }, store.Sets);
        Assert.Equal(new[] { OpenAiCompatibleChatProvider.ApiKeyCredentialKey }, store.Deletes);
        Assert.Contains(profileKey, store.HasChecks);
        Assert.Equal("legacy-key", await store.PeekAsync(profileKey));
        Assert.False(await store.ContainsAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey));

        var getsAfterMigration = store.Gets.Count;
        var setsAfterMigration = store.Sets.Count;

        var second = await provider.CompleteAsync(Request());

        Assert.Null(second.Error);
        Assert.Equal("Bearer legacy-key", handler.Requests[1].Authorization);
        Assert.Equal(setsAfterMigration, store.Sets.Count);
        Assert.Equal(profileKey, store.Gets[^1]);
        Assert.DoesNotContain(
            OpenAiCompatibleChatProvider.ApiKeyCredentialKey,
            store.Gets.Skip(getsAfterMigration));
    }

    [Fact]
    public async Task OpenRouterNeverReadsOrInheritsLegacyCredential()
    {
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");

        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(providerId: ProviderCatalog.OpenRouterId), store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
        Assert.DoesNotContain(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, store.Gets);
        Assert.DoesNotContain(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, store.HasChecks);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
        Assert.Equal("legacy-key", await store.PeekAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey));
    }

    [Fact]
    public async Task OpenRouterUsesItsOwnProfileCredential()
    {
        var profileKey = ProviderCredentials.GetApiKeyCredentialKey(ProviderCatalog.OpenRouter);
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");
        await store.SeedAsync(profileKey, "router-key");

        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(providerId: ProviderCatalog.OpenRouterId), store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Equal("Bearer router-key", Assert.Single(handler.Requests).Authorization);
        Assert.DoesNotContain(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, store.Gets);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("openrouter")]
    public async Task CloudProfileWithoutCredentialSendsUnauthenticatedRequestAndSurfacesServerAuthFailure(
        string providerId)
    {
        using var handler = FakeHttpMessageHandler.Json(
            """{"error":{"message":"missing key"}}""",
            HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        var store = new TrackingCredentialStore();
        var provider = CreateProvider(client, Settings(providerId: providerId), store);

        var result = await provider.CompleteAsync(Request());

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorKind.Authentication, result.Error!.Kind);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task UnknownRemoteProviderNeverInheritsLegacyCredential()
    {
        const string providerId = "custom-openai";
        var store = new TrackingCredentialStore();
        await store.SeedAsync(OpenAiCompatibleChatProvider.ApiKeyCredentialKey, "legacy-key");

        using var handler = FakeHttpMessageHandler.Json(CompletionJson);
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client, Settings(providerId: providerId), store);

        var result = await provider.CompleteAsync(Request());

        Assert.Null(result.Error);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
        Assert.Equal(
            new[] { ProviderCredentials.GetApiKeyCredentialKey(providerId) },
            store.Gets);
        Assert.Empty(store.Sets);
        Assert.Empty(store.Deletes);
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

        return CreateProvider(client, settings ?? Settings(), credentialStore);
    }

    private static OpenAiCompatibleChatProvider CreateProvider(
        HttpClient client,
        AppSettings settings,
        ISecureCredentialStore credentialStore)
    {
        return new OpenAiCompatibleChatProvider(
            client,
            new FakeAppSettingsStore(settings),
            credentialStore);
    }

    private static AppSettings Settings(
        string baseUrl = BaseUrl,
        string model = Model,
        bool streaming = true,
        int timeoutSeconds = 30,
        string providerId = ProviderCatalog.OpenAiId,
        bool freeModelsOnly = false,
        bool allowCloudInference = true)
    {
        var defaults = CapriDefaultSettings.Create();
        return defaults with
        {
            Provider = defaults.Provider with
            {
                ProviderId = providerId,
                BaseUrl = baseUrl,
                ChatModel = model,
                EnableStreaming = streaming,
                RequestTimeoutSeconds = timeoutSeconds,
                FreeModelsOnly = freeModelsOnly,
                AllowCloudInference = allowCloudInference,
            },
        };
    }

    private sealed class TrackingCredentialStore : ISecureCredentialStore
    {
        private readonly FakeCredentialStore _inner = new();

        public List<string> Gets { get; } = [];

        public List<string> HasChecks { get; } = [];

        public List<string> Sets { get; } = [];

        public List<string> Deletes { get; } = [];

        public Task<bool> HasAsync(string key, CancellationToken cancellationToken = default)
        {
            HasChecks.Add(key);
            return _inner.HasAsync(key, cancellationToken);
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            Gets.Add(key);
            return _inner.GetAsync(key, cancellationToken);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Sets.Add(key);
            return _inner.SetAsync(key, value, cancellationToken);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Deletes.Add(key);
            return _inner.DeleteAsync(key, cancellationToken);
        }

        public Task SeedAsync(string key, string value)
        {
            return _inner.SetAsync(key, value);
        }

        public Task<string?> PeekAsync(string key)
        {
            return _inner.GetAsync(key);
        }

        public Task<bool> ContainsAsync(string key)
        {
            return _inner.HasAsync(key);
        }
    }

    private static ChatRequest Request(
        string content = "Hi",
        string? model = null,
        bool requireJsonObject = false)
    {
        return new ChatRequest(
            [new ChatMessage(ChatRole.User, content)],
            ModelId: model,
            RequireJsonObject: requireJsonObject);
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
