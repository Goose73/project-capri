using OpenCvSharp;

namespace Capri.Desktop.Services;

public interface IAmbientCameraCapture
{
    /// <summary>
    /// Captures exactly one low-resolution JPEG into memory. Camera is only
    /// opened on explicit session start; no recording or disk writes.
    /// </summary>
    Task<byte[]> CaptureJpegAsync(int cameraIndex, CancellationToken cancellationToken);
}

/// <summary>
/// One-shot DirectShow webcam capture. The device is released after each
/// snapshot, so a paused/stopped companion never holds a streaming camera.
/// </summary>
public sealed class OpenCvAmbientCameraCapture : IAmbientCameraCapture
{
    public Task<byte[]> CaptureJpegAsync(int cameraIndex, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Ambient webcam capture requires Windows.");
        if (cameraIndex is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(cameraIndex));
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var camera = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                if (!camera.IsOpened())
                    throw new InvalidOperationException(
                        "Webcam unavailable. Check Windows Camera privacy settings and the camera index.");

                camera.Set(VideoCaptureProperties.FrameWidth, 640);
                camera.Set(VideoCaptureProperties.FrameHeight, 480);
                using var frame = new Mat();
                if (!camera.Read(frame) || frame.Empty())
                    throw new InvalidOperationException("The webcam did not provide an image.");
                cancellationToken.ThrowIfCancellationRequested();

                using var resized = new Mat();
                var source = frame;
                if (frame.Width > 640 || frame.Height > 480)
                {
                    Cv2.Resize(frame, resized, new OpenCvSharp.Size(640, 480));
                    source = resized;
                }

                Cv2.ImEncode(".jpg", source, out var jpeg,
                    new ImageEncodingParam(ImwriteFlags.JpegQuality, 60));
                if (jpeg.Length is < 128 or > 450_000)
                    throw new InvalidOperationException("Camera frame could not be encoded safely.");
                cancellationToken.ThrowIfCancellationRequested();
                return jpeg;
            }
            catch (OpenCVException)
            {
                throw new InvalidOperationException(
                    "Webcam capture failed. Select another camera or review Windows permissions.");
            }
            catch (DllNotFoundException)
            {
                throw new InvalidOperationException(
                    "The Windows webcam runtime is unavailable. Reinstall Capri's OpenCV dependencies.");
            }
        }, cancellationToken);
    }
}
