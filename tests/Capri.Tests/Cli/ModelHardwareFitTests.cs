using Capri.Cli.Commands;
using Xunit;

namespace Capri.Tests.Cli;

public sealed class ModelHardwareFitTests
{
    [Theory]
    [InlineData(null, 6)]
    [InlineData(16, null)]
    [InlineData(null, null)]
    public void UnknownHardwareNeverClaimsGoodFit(double? ramGb, double? vramGb)
    {
        var fit = ModelHardwareFit.ForFourB(ramGb, vramGb);
        Assert.Contains("unknown", fit);
        Assert.DoesNotContain("good fit", fit);
    }

    [Fact]
    public void SufficientMeasuredResourcesAllowQualifiedRecommendation()
    {
        Assert.Contains("good fit estimate", ModelHardwareFit.ForFourB(16, 6));
    }

    [Fact]
    public void LowRamDoesNotRecommendFourB()
    {
        Assert.Contains("limited system RAM", ModelHardwareFit.ForFourB(4, 6));
    }

    [Fact]
    public void LowVramDoesNotClaimGoodFit()
    {
        Assert.Contains("CPU offload", ModelHardwareFit.ForFourB(16, 2));
    }
}
