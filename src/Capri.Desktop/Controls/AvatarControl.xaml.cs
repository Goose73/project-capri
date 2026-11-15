using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Capri.Core.Avatar;
using Capri.Core.Models;

namespace Capri.Desktop.Controls;

/// <summary>
/// Image-backed Capri performance renderer. The installed private character
/// sprite is transformed as a whole, while optional cleared full-frame clips
/// can supply real eye/mouth/pose drawings without painting a substitute face.
/// </summary>
public partial class AvatarControl : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(AvatarState), typeof(AvatarControl),
        new PropertyMetadata(AvatarState.Idle, OnStateChanged));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(AvatarControl),
        new PropertyMetadata(false, OnStateChanged));

    private Storyboard? _idleMotion;
    private Storyboard? _gestureMotion;
    private Storyboard? _faceMotion;
    private ImageSource? _baseSprite;
    private long _motionVersion;

    public AvatarControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            PlayGesture(AvatarGesture.Appear);
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Refresh();
                PlayGesture(AvatarGesture.Appear);
            }
            else
            {
                StopMotion();
            }
        };
        Unloaded += (_, _) => StopMotion();
    }

    public AvatarState State
    {
        get => (AvatarState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    /// <summary>
    /// Gesture availability is visual-only, not an indication of screen or
    /// microphone access. Eye and mouth clips need user-approved frame art.
    /// </summary>
    public bool CanPerformGesture(AvatarGesture gesture)
    {
        var clip = AvatarGesturePlanner.ForGesture(gesture, ReduceMotion);
        return clip is not null && State != AvatarState.Sleep && IsVisible &&
               AvatarImage.Visibility == Visibility.Visible &&
               (!clip.RequiresArtworkFrames || HasClipFrames(gesture));
    }

    /// <summary>
    /// Interrupt current gesture; play a short bounded pose sequence and then
    /// resume the CURRENT state animation. No desktop-window repositioning.
    /// </summary>
    public bool PlayGesture(AvatarGesture gesture, Action? onFinished = null)
    {
        if (!CanPerformGesture(gesture)) return false;
        var clip = AvatarGesturePlanner.ForGesture(gesture, ReduceMotion)!;
        var frames = clip.RequiresArtworkFrames ? FindClipFrames(gesture) : [];
        if (clip.RequiresArtworkFrames && frames.Count < 2) return false;

        StopMotion();
        ResetPose();

        var storyboard = new Storyboard
        {
            FillBehavior = FillBehavior.Stop
        };
        AddPoseTrack(storyboard, MotionTranslate, TranslateTransform.XProperty, clip.Frames, x => x.X);
        AddPoseTrack(storyboard, MotionTranslate, TranslateTransform.YProperty, clip.Frames, x => x.Y);
        AddPoseTrack(storyboard, MotionRotate, RotateTransform.AngleProperty, clip.Frames, x => x.Angle);
        AddPoseTrack(storyboard, MotionScale, ScaleTransform.ScaleXProperty, clip.Frames, x => x.ScaleX);
        AddPoseTrack(storyboard, MotionScale, ScaleTransform.ScaleYProperty, clip.Frames, x => x.ScaleY);
        AddPoseTrack(storyboard, SpriteHost, UIElement.OpacityProperty, clip.Frames, x => x.Opacity);
        AddPoseTrack(storyboard, EchoImage, UIElement.OpacityProperty, clip.Frames, x => x.Echo);
        if (frames.Count >= 2)
            AddSpriteFrames(storyboard, frames, clip.DurationMs);

        var version = _motionVersion;
        _gestureMotion = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (version != _motionVersion || !ReferenceEquals(_gestureMotion, storyboard))
                return; // newer request, state change, hidden window or stop took precedence
            _gestureMotion = null;
            AvatarImage.Source = _baseSprite;
            ResetPose();
            onFinished?.Invoke();
            if (IsVisible && !ReduceMotion)
                StartStateMotion();
        };
        storyboard.Begin();
        return true;
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control) control.Refresh();
    }

    private void Refresh()
    {
        try
        {
            var path = FindAssetPath(State);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _baseSprite = LoadImage(path);
                AvatarImage.Source = _baseSprite;
                EchoImage.Source = _baseSprite;
                AvatarImage.Visibility = Visibility.Visible;
                AssetPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                _baseSprite = null;
                AvatarImage.Source = null;
                EchoImage.Source = null;
                AvatarImage.Visibility = Visibility.Collapsed;
                AssetPlaceholder.Visibility = Visibility.Visible;
            }
            ApplyLightweightMotion();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or
                                   System.Runtime.InteropServices.COMException)
        {
            _baseSprite = null;
            AvatarImage.Source = null;
            EchoImage.Source = null;
            AvatarImage.Visibility = Visibility.Collapsed;
            AssetPlaceholder.Visibility = Visibility.Visible;
            StopMotion();
        }
    }

    private void ApplyLightweightMotion()
    {
        StopMotion();
        ResetPose();
        StartStateMotion();
    }

    private void StartStateMotion()
    {
        var profile = AvatarMotionPlanner.ForState(State, ReduceMotion);
        if (!profile.Animated || !IsVisible ||
            AvatarImage.Visibility != Visibility.Visible) return;

        var duration = TimeSpan.FromMilliseconds(profile.HalfCycleMilliseconds);
        var storyboard = new Storyboard
        {
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = true
        };
        AddLoopTrack(storyboard, MotionTranslate, TranslateTransform.YProperty,
            0, profile.BobPixels, duration);
        AddLoopTrack(storyboard, MotionTranslate, TranslateTransform.XProperty,
            -profile.SwayPixels, profile.SwayPixels, duration);
        AddLoopTrack(storyboard, MotionRotate, RotateTransform.AngleProperty,
            -profile.TiltDegrees, profile.TiltDegrees, duration);
        AddLoopTrack(storyboard, MotionScale, ScaleTransform.ScaleYProperty,
            1, profile.StretchY, duration);

        _idleMotion = storyboard;
        storyboard.Begin();
        StartOptionalFaceMotion();
    }

    private void StartOptionalFaceMotion()
    {
        // Optional approved full-frame sprites. Without them, no simulated
        // blink, mouth or facial-feature geometry is drawn over Capri's eyes.
        var gesture = State == AvatarState.Talking ? AvatarGesture.MouthSpeak
            : State is AvatarState.Idle or AvatarState.Listening or AvatarState.Thinking
                ? AvatarGesture.Blink : (AvatarGesture?)null;
        if (gesture is null) return;

        var frames = FindClipFrames(gesture.Value);
        if (frames.Count < 2) return;

        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        if (gesture == AvatarGesture.MouthSpeak)
        {
            AddSpriteFrames(storyboard, frames, 740, initialDelayMs: 0);
        }
        else
        {
            // One quick illustrated blink every ~5 seconds; no flashing.
            AddSpriteFrames(storyboard, frames, 4800, initialDelayMs: 4090);
        }
        _faceMotion = storyboard;
        storyboard.Begin();
    }

    private static void AddLoopTrack(
        Storyboard storyboard, DependencyObject target, DependencyProperty property,
        double from, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private static void AddPoseTrack(
        Storyboard storyboard, DependencyObject target, DependencyProperty property,
        IReadOnlyList<AvatarGestureFrame> frames,
        Func<AvatarGestureFrame, double> selector)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop
        };
        foreach (var frame in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                selector(frame),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(frame.AtMs)),
                new PowerEase { Power = 3.5, EasingMode = EasingMode.EaseOut }));
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void AddSpriteFrames(
        Storyboard storyboard, List<BitmapImage> frames,
        int durationMs, int initialDelayMs = 0)
    {
        var track = new ObjectAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(durationMs)
        };
        var available = Math.Max(100, durationMs - initialDelayMs - 90);
        track.KeyFrames.Add(new DiscreteObjectKeyFrame(_baseSprite,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        for (var i = 0; i < frames.Count; i++)
        {
            var ms = initialDelayMs + (int)((i + 0.0) * available / frames.Count);
            track.KeyFrames.Add(new DiscreteObjectKeyFrame(frames[i],
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        }
        track.KeyFrames.Add(new DiscreteObjectKeyFrame(_baseSprite,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs))));
        Storyboard.SetTarget(track, AvatarImage);
        Storyboard.SetTargetProperty(track, new PropertyPath(Image.SourceProperty));
        storyboard.Children.Add(track);
    }

    private void StopMotion()
    {
        ++_motionVersion;
        try { _idleMotion?.Stop(); } catch (InvalidOperationException) { }
        try { _gestureMotion?.Stop(); } catch (InvalidOperationException) { }
        try { _faceMotion?.Stop(); } catch (InvalidOperationException) { }
        _idleMotion = null;
        _gestureMotion = null;
        _faceMotion = null;
        AvatarImage.Source = _baseSprite;
        ResetPose();
    }

    private void ResetPose()
    {
        MotionTranslate.X = 0;
        MotionTranslate.Y = 0;
        MotionScale.ScaleX = 1;
        MotionScale.ScaleY = 1;
        MotionRotate.Angle = 0;
        SpriteHost.Opacity = 1;
        EchoImage.Opacity = 0;
    }

    private static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool HasClipFrames(AvatarGesture gesture)
    {
        var folderName = gesture.ToString().ToLowerInvariant();
        foreach (var root in GetCandidateRoots())
        {
            var directory = Path.Combine(root, "design", "assets", "capri", "clips", folderName);
            if (!Directory.Exists(directory)) continue;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return false;
                for (var index = 0; index < 2; index++)
                {
                    var file = Path.Combine(directory, $"frame-{index:00}.png");
                    if (!File.Exists(file)) return false;
                    var info = new FileInfo(file);
                    if (info.Length is <= 0 or > 8_000_000 ||
                        (info.Attributes & FileAttributes.ReparsePoint) != 0)
                        return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
        return false;
    }

    private static List<BitmapImage> FindClipFrames(AvatarGesture gesture)
    {
        // Strictly named, user-provided animation frames are private, ignored
        // by git. Never load arbitrary scripts, URLs or rasterized show footage.
        var folderName = gesture.ToString().ToLowerInvariant();
        foreach (var root in GetCandidateRoots())
        {
            var directory = Path.Combine(root, "design", "assets", "capri", "clips", folderName);
            if (!Directory.Exists(directory)) continue;

            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return [];
                var frames = new List<BitmapImage>();
                for (var index = 0; index < 12; index++)
                {
                    var file = Path.Combine(directory, $"frame-{index:00}.png");
                    if (!File.Exists(file)) break;
                    var info = new FileInfo(file);
                    if (info.Length is <= 0 or > 8_000_000 ||
                        (info.Attributes & FileAttributes.ReparsePoint) != 0)
                        return [];
                    frames.Add(LoadImage(file));
                }
                return frames.Count >= 2 ? frames : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException or
                                       System.Runtime.InteropServices.COMException)
            {
                return [];
            }
        }
        return [];
    }

    private static string? FindAssetPath(AvatarState state)
    {
        foreach (var root in GetCandidateRoots())
        {
            var dir = Path.Combine(root, "design", "assets", "capri");
            if (!Directory.Exists(dir)) continue;
            foreach (var name in GetCandidateNames(state))
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string?> candidates =
        [
            AppContext.BaseDirectory,
            @"C:\Project Capri",
            Environment.CurrentDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."))
        ];
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string full;
            try { full = Path.GetFullPath(candidate); } catch { continue; }
            if (seen.Add(full)) yield return full;
        }
    }

    private static IEnumerable<string> GetCandidateNames(AvatarState state)
    {
        var keys = state switch
        {
            AvatarState.Listening => new[] { "listening", "idle" },
            AvatarState.Thinking => new[] { "thinking", "idle" },
            AvatarState.Talking => new[] { "talking", "speaking", "idle" },
            AvatarState.Success or AvatarState.Happy => new[] { "happy", "success", "idle" },
            AvatarState.Error or AvatarState.Annoyed => new[] { "annoyed", "error", "idle" },
            AvatarState.Surprised => new[] { "surprised", "idle" },
            AvatarState.Sleep => new[] { "sleep", "inactive", "idle" },
            _ => new[] { "idle" }
        };
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return key + ".png";
            yield return key + ".webp";
        }
    }
}
