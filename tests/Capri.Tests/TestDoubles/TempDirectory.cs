using System.IO;

namespace Capri.Tests.TestDoubles;

/// <summary>
/// Creates and removes an isolated temporary directory for a test.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Capri.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS will eventually remove temp data.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
