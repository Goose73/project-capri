using System.Text.Json;
using Capri.Core.Models;
using Capri.Desktop.Services;

namespace Capri.Tests.Desktop;

public sealed class CapriNeuralVoiceSettingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("http://127.0.0.1:5000/synthesize", true)]
    [InlineData("http://127.0.0.1:5001/synthesize", false)]
    [InlineData("http://localhost:5000/synthesize", false)]
    [InlineData("http://192.168.1.2:5000/synthesize", false)]
    [InlineData("https://127.0.0.1:5000/synthesize", false)]
    [InlineData("http://127.0.0.1:5000/other", false)]
    [InlineData("http://127.0.0.1:5000/synthesize?target=remote", false)]
    public void BuiltInVoiceOnlyBootstrapsItsOwnFixedLoopbackUri(string address, bool expected)
    {
        Assert.Equal(expected, CapriLocalVoiceBootstrapper.IsCapriEndpoint(new Uri(address)));
    }

    [Fact]
    public void NamedLocalVoiceRoundTripsThroughSettingsWithoutLosingOtherPreferences()
    {
        var old = new AppSettings
        {
            Voice = new AppSettings.VoiceSettings
            {
                Enabled = true,
                EnableWakeWord = true,
                UseLocalNeuralVoice = true,
                NeuralVoiceEndpoint = "http://127.0.0.1:5000",
                NeuralVoiceName = "af_heart",
                Volume = 0.9,
                SpeechRate = 1.05
            }
        };
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(old, JsonOptions), JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal("af_heart", roundTrip.Voice.NeuralVoiceName);
        Assert.Equal(0.9, roundTrip.Voice.Volume);
        Assert.True(roundTrip.Voice.EnableWakeWord);
    }

    [Fact]
    public void OlderSettingsWithoutVoiceNameKeepEmptyServerDefault()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"voice":{"enabled":true,"useLocalNeuralVoice":false}}""", JsonOptions);
        Assert.NotNull(settings);
        Assert.Equal(string.Empty, settings.Voice.NeuralVoiceName);
    }
}
