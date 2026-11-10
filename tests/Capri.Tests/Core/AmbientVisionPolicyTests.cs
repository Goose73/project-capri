using Capri.Core.Companion;
using Capri.Core.Models;

namespace Capri.Tests.Core;

public sealed class AmbientVisionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static AmbientVisionSettings Enabled() =>
        new() { Enabled = true, AllowSpontaneousComments = true };

    private static AmbientVisionRuntime Permitted() => new(
        SessionConsentGiven: true,
        WindowsCameraAccessGranted: true,
        CameraAvailable: true,
        VisibleIndicatorActive: true,
        LocalVisionModelAvailable: true,
        Paused: false,
        SessionLocked: false,
        CaptureInProgress: false,
        LastCapturedUtc: null,
        LastCommentUtc: null,
        NewMeaningfulObservation: false,
        UserIsBusy: false);

    [Fact]
    public void DefaultsNeverStartCameraOrSpontaneousSpeech()
    {
        var settings = new AppSettings();
        Assert.False(settings.AmbientVision.Enabled);
        Assert.False(settings.AmbientVision.AllowSpontaneousComments);
        Assert.True(settings.AmbientVision.LocalOnly);
        Assert.Equal(20, settings.AmbientVision.SampleIntervalSeconds);
        var result = AmbientVisionPolicy.Evaluate(settings.AmbientVision, Permitted(), Now);
        Assert.False(result.CaptureFrame);
        Assert.False(result.MakeSpontaneousRemark);
    }

    [Fact]
    public void StoredPreferenceDoesNotReplaceFreshRuntimeConsent()
    {
        var result = AmbientVisionPolicy.Evaluate(Enabled(),
            Permitted() with { SessionConsentGiven = false }, Now);
        Assert.False(result.CaptureFrame);
        Assert.Contains("consent", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Windows camera permission", false, true, true, true, false, false)]
    [InlineData("Camera device", true, false, true, true, false, false)]
    [InlineData("Visible indicator", true, true, false, true, false, false)]
    [InlineData("Local model", true, true, true, false, false, false)]
    [InlineData("Paused", true, true, true, true, true, false)]
    [InlineData("Locked", true, true, true, true, false, true)]
    public void MissingAnyPermissionOrSafetyConditionBlocksEverything(
        string _, bool windows, bool camera, bool indicator,
        bool local, bool paused, bool locked)
    {
        var runtime = Permitted() with
        {
            WindowsCameraAccessGranted = windows,
            CameraAvailable = camera,
            VisibleIndicatorActive = indicator,
            LocalVisionModelAvailable = local,
            Paused = paused,
            SessionLocked = locked,
            NewMeaningfulObservation = true,
            LastCapturedUtc = Now.AddSeconds(-10),
        };
        var result = AmbientVisionPolicy.Evaluate(Enabled(), runtime, Now);
        Assert.False(result.CaptureFrame);
        Assert.False(result.MakeSpontaneousRemark);
    }

    [Fact]
    public void CloudProcessingIsNotAnOption()
    {
        var settings = Enabled() with { LocalOnly = false };
        var result = AmbientVisionPolicy.Evaluate(settings, Permitted(), Now);
        Assert.False(result.CaptureFrame);
    }

    [Fact]
    public void SingleSnapshotCanBeEligibleButNotAtVideoFrameRate()
    {
        var config = Enabled();
        var first = AmbientVisionPolicy.Evaluate(config, Permitted(), Now);
        Assert.True(first.CaptureFrame);
        var tooSoon = AmbientVisionPolicy.Evaluate(
            config, Permitted() with { LastCapturedUtc = Now.AddSeconds(-10) }, Now);
        Assert.False(tooSoon.CaptureFrame);
        var later = AmbientVisionPolicy.Evaluate(
            config, Permitted() with { LastCapturedUtc = Now.AddSeconds(-20) }, Now);
        Assert.True(later.CaptureFrame);
    }

    [Fact]
    public void CannotSampleWhilePriorInferenceInProgress()
    {
        var result = AmbientVisionPolicy.Evaluate(Enabled(),
            Permitted() with { CaptureInProgress = true }, Now);
        Assert.False(result.CaptureFrame);
    }

    [Fact]
    public void CommentsNeedNewSuccessfulObservationAndQuietCooldown()
    {
        var runtime = Permitted() with
        {
            LastCapturedUtc = Now.AddSeconds(-5),
            NewMeaningfulObservation = true,
            LastCommentUtc = Now.AddMinutes(-6),
        };
        Assert.True(AmbientVisionPolicy.Evaluate(Enabled(), runtime, Now).MakeSpontaneousRemark);
        Assert.False(AmbientVisionPolicy.Evaluate(Enabled(),
            runtime with { LastCommentUtc = Now.AddSeconds(-60) }, Now).MakeSpontaneousRemark);
        Assert.False(AmbientVisionPolicy.Evaluate(Enabled(),
            runtime with { UserIsBusy = true }, Now).MakeSpontaneousRemark);
        Assert.False(AmbientVisionPolicy.Evaluate(Enabled(),
            runtime with { NewMeaningfulObservation = false }, Now).MakeSpontaneousRemark);
        Assert.False(AmbientVisionPolicy.Evaluate(Enabled(),
            runtime with { LastCapturedUtc = Now.AddMinutes(-3) }, Now).MakeSpontaneousRemark);
        Assert.False(AmbientVisionPolicy.Evaluate(Enabled() with
        {
            AllowSpontaneousComments = false
        }, runtime, Now).MakeSpontaneousRemark);
    }

    [Fact]
    public void DangerousIntervalValuesCannotOverrideMinimums()
    {
        var config = Enabled() with { SampleIntervalSeconds = 0, MinimumCommentIntervalSeconds = 0 };
        var earlierSample = Now.AddSeconds(-8);
        var runtime = Permitted() with
        {
            LastCapturedUtc = earlierSample,
            LastCommentUtc = Now.AddSeconds(-90),
            NewMeaningfulObservation = true,
        };
        var decision = AmbientVisionPolicy.Evaluate(config, runtime, Now);
        Assert.False(decision.CaptureFrame);
        Assert.False(decision.MakeSpontaneousRemark);
    }
}
