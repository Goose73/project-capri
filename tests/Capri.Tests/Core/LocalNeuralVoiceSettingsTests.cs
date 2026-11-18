using Capri.Core.Voice;

namespace Capri.Tests.Core;

public sealed class LocalNeuralVoiceSettingsTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5000", "http://127.0.0.1:5000/synthesize")]
    [InlineData("http://127.0.0.1:5100/", "http://127.0.0.1:5100/synthesize")]
    [InlineData("http://[::1]:5000", "http://[::1]:5000/synthesize")]
    public void AcceptsOnlyLiteralLoopbackHttpAddress(string input, string expected)
    {
        Assert.True(LocalNeuralVoiceSettings.TryGetSynthesisUri(input, out var uri));
        Assert.Equal(expected, uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://127.0.0.1:5000")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://example.com:5000")]
    [InlineData("http://192.168.1.12:5000")]
    [InlineData("http://127.0.0.1:5000/redirect")]
    [InlineData("http://127.0.0.1:5000/?token=test")]
    [InlineData("http://127.0.0.1:5000/#fragment")]
    [InlineData("http://user:pass@127.0.0.1:5000")]
    [InlineData("file:///C:/test.wav")]
    [InlineData("http://127.0.0.1:21")]
    [InlineData("")]
    public void RejectsRemoteOrUnexpectedVoiceAddresses(string input)
    {
        Assert.False(LocalNeuralVoiceSettings.TryGetSynthesisUri(input, out var uri));
        Assert.Null(uri);
    }
}
