using System.Globalization;

namespace Capri.Security;

/// <summary>
/// Writes files through a same-directory temporary file followed by an atomic
/// move so readers never observe a partially written document.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/>, creating
    /// the parent directory when necessary.
    /// </summary>
    public static async Task WriteAllBytesAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = string.Concat(
            path,
            ".",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            ".tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temp file is undesirable but must never mask the original failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: cleanup is best effort.
        }
    }
}
