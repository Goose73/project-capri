namespace Capri.Tests;

/// <summary>
/// Scaffold-level smoke tests that prove the solution assembles and projects resolve.
/// </summary>
public sealed class ScaffoldSmokeTests
{
    [Theory]
    [InlineData(typeof(Capri.Core.AssemblyMarker), "Capri.Core")]
    [InlineData(typeof(Capri.Providers.AssemblyMarker), "Capri.Providers")]
    [InlineData(typeof(Capri.Security.AssemblyMarker), "Capri.Security")]
    [InlineData(typeof(Capri.Automation.AssemblyMarker), "Capri.Automation")]
    [InlineData(typeof(Capri.Voice.AssemblyMarker), "Capri.Voice")]
    public void AssemblyMarkerResolvesFromExpectedAssembly(Type markerType, string expectedAssemblyName)
    {
        Assert.Equal(expectedAssemblyName, markerType.Assembly.GetName().Name);
    }

    [Fact]
    public void DesktopAssemblyIsReferenced()
    {
        Assert.Equal("Capri.Desktop", typeof(Capri.Desktop.App).Assembly.GetName().Name);
    }
}
