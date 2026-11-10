using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Capri.Desktop.Services;
using Capri.Tests.TestDoubles;

namespace Capri.Tests.Desktop;

public sealed class LocalOllamaAmbientVisionTests
{
    private static readonly byte[] MinimalJpeg =
        [0xFF, 0xD8, .. Enumerable.Repeat((byte)1, 200), 0xFF, 0xD9];

    [Fact]
    public async Task RequestsAlwaysUseLoopbackOllamaAndOnlyTransientCameraImages()
    {
        var handler = new FakeHttpMessageHandler((request, index, ct) =>
        {
            var json = index == 0
                ? """{"models":[{"name":"qwen3.5:4b"}]}"""
                : """{"message":{"content":"{\"meaningful\":true,\"remark\":\"A colourful model is on the desk!\"}"}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        using var http = new HttpClient(handler);
        using var vision = new LocalOllamaAmbientVision(http);

        Assert.True(await vision.IsModelReadyAsync("qwen3.5:4b", CancellationToken.None));
        var observation = await vision.ObserveAsync("qwen3.5:4b", MinimalJpeg, CancellationToken.None);
        Assert.True(observation.Meaningful);
        Assert.Equal("A colourful model is on the desk!", observation.Remark);

        Assert.Equal(2, handler.RequestCount);
        Assert.All(handler.Requests, call =>
        {
            Assert.Equal("127.0.0.1", call.RequestUri?.Host);
            Assert.Equal(11434, call.RequestUri?.Port);
            Assert.Equal("http", call.RequestUri?.Scheme);
            Assert.Null(call.Authorization);
        });
        Assert.EndsWith("/api/tags", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.EndsWith("/api/chat", handler.Requests[1].RequestUri!.AbsolutePath);
        using var sent = JsonDocument.Parse(handler.Requests[1].Body!);
        var image = sent.RootElement.GetProperty("messages")[0].GetProperty("images")[0].GetString();
        Assert.Equal(MinimalJpeg, Convert.FromBase64String(image!));
        Assert.False(sent.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public void RejectsInvalidModelAndUnboundedFrameBeforeAnyRequest()
    {
        Assert.False(LocalOllamaAmbientVision.IsValidModel("qwen3.5:4b\nhttps://evil"));
        Assert.False(LocalOllamaAmbientVision.IsValidModel(""));
        Assert.True(LocalOllamaAmbientVision.IsValidModel("gemma3:4b"));

        var handler = FakeHttpMessageHandler.Json("{}");
        using var http = new HttpClient(handler);
        using var vision = new LocalOllamaAmbientVision(http);
        Assert.ThrowsAsync<InvalidDataException>(() =>
            vision.ObserveAsync("qwen3.5:4b", [0xFF, 0xD8], CancellationToken.None)).GetAwaiter().GetResult();
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("""{"message":{"content":"{\"meaningful\":false,\"remark\":\"\"}"}}""")]
    [InlineData("""{"message":{"content":"not json"}}""")]
    [InlineData("""{"message":{"content":"{\"meaningful\":true,\"remark\":\"\"}"}}""")]
    public async Task UnreliableModelRemarksNeverProduceSpontaneousComments(string modelResponse)
    {
        var handler = FakeHttpMessageHandler.Json(modelResponse);
        using var http = new HttpClient(handler);
        using var vision = new LocalOllamaAmbientVision(http);
        var result = await vision.ObserveAsync("qwen3.5:4b", MinimalJpeg, CancellationToken.None);
        Assert.False(result.Meaningful);
    }
}
