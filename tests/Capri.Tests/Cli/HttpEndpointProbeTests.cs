using Capri.Cli.Services;

namespace Capri.Tests.Cli;

public sealed class HttpEndpointProbeTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/models")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/v1/models")]
    [InlineData("http://localhost:11434", "http://localhost:11434/models")]
    [InlineData("http://localhost:11434/v1/models", "http://localhost:11434/v1/models")]
    [InlineData("http://localhost:11434/v1/chat/completions", "http://localhost:11434/v1/models")]
    public void BuildModelsUriNormalizesOpenAiCompatibleBaseUrls(string input, string expected)
    {
        var uri = HttpEndpointProbe.BuildModelsUri(new Uri(input));

        Assert.Equal(expected, uri.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void BuildModelsUriDropsQueryAndFragment()
    {
        var uri = HttpEndpointProbe.BuildModelsUri(
            new Uri("http://localhost:11434/v1?api_key=secret#fragment"));

        Assert.DoesNotContain("api_key", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fragment", uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("http://localhost:11434/v1/models", uri.AbsoluteUri);
    }
}
