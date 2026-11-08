using Capri.Automation.Autonomy;

namespace Capri.Tests.Autonomy;

public sealed class WindowObservationTests
{
    [Fact]
    public void PackageFamilyNameFromBufferExcludesApiReportedNullTerminator()
    {
        const string packageFamily = "Microsoft.WindowsNotepad_8wekyb3d8bbwe";
        var buffer = (packageFamily + "\0").ToCharArray();

        var result = WindowObservation.PackageFamilyNameFromBuffer(buffer, (uint)buffer.Length);

        Assert.Equal(packageFamily, result);
    }

    [Fact]
    public void PackageFamilyNameFromBufferStopsAtFirstNullWithinReportedLength()
    {
        var buffer = "trusted\0unrelated".ToCharArray();

        var result = WindowObservation.PackageFamilyNameFromBuffer(buffer, (uint)buffer.Length);

        Assert.Equal("trusted", result);
    }
}
