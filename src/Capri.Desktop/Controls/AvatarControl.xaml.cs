using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Capri.Core.Avatar;
using Capri.Core.Models;

namespace Capri.Desktop.Controls;

public partial class AvatarControl : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(AvatarState),
        typeof(AvatarControl),
        new PropertyMetadata(AvatarState.Idle, OnStateChanged));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion),
        typeof(bool),
        typeof(AvatarControl),
        new PropertyMetadata(false, OnStateChanged));

    private Storyboard? _idleMotion;
    private Storyboard? _arrivalMotion;

    public AvatarControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            PlayArrivalMotion();
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Refresh();
                PlayArrivalMotion();
            }
            else
            {
                StopMotion();
            }
        };
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

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarControl control)
        {
            control.Refresh();
        }
    }

    private void Refresh()
    {
        try
        {
            var path = FindAssetPath(State);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                AvatarImage.Source = LoadImage(path);
                AvatarImage.Visibility = Visibility.Visible;
                AssetPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AvatarImage.Source = null;
                AvatarImage.Visibility = Visibility.Collapsed;
                AssetPlaceholder.Visibility = Visibility.Visible;
            }

            ApplyLightweightMotion();
        }
        catch
        {
            AvatarImage.Source = null;
            AvatarImage.Visibility = Visibility.Collapsed;
            AssetPlaceholder.Visibility = Visibility.Visible;
            StopMotion();
        }
    }

    private void ApplyLightweightMotion()
    {
        StopMotion();
        MotionTranslate.X = 0;
        MotionTranslate.Y = 0;
        MotionScale.ScaleX = 1;
        MotionScale.ScaleY = 1;
        MotionRotate.Angle = 0;

        var profile = AvatarMotionPlanner.ForState(State, ReduceMotion);
        // Do not animate the missing-artwork notice. The original character
        // requires approved artwork, not geometry drawn to impersonate it.
        if (!profile.Animated || !IsVisible || AvatarImage.Visibility != Visibility.Visible)
            return;

        var duration = TimeSpan.FromMilliseconds(profile.HalfCycleMilliseconds);
        var storyboard = new Storyboard
        {
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = true
        };
        AddMotion(storyboard, MotionTranslate, TranslateTransform.YProperty,
            0, profile.BobPixels, duration);
        AddMotion(storyboard, MotionTranslate, TranslateTransform.XProperty,
            -profile.SwayPixels, profile.SwayPixels, duration);
        AddMotion(storyboard, MotionRotate, RotateTransform.AngleProperty,
            -profile.TiltDegrees, profile.TiltDegrees, duration);
        AddMotion(storyboard, MotionScale, ScaleTransform.ScaleYProperty,
            1, profile.StretchY, duration);

        _idleMotion = storyboard;
        storyboard.Begin();
    }

    private void PlayArrivalMotion()
    {
        if (!IsVisible || ReduceMotion || AvatarImage.Visibility != Visibility.Visible)
        {
            SpriteHost.Opacity = 1;
            return;
        }

        // A small reveal is possible with a single sprite. The full
        // Capri effect-entity emergence needs separately licensed art.
        _arrivalMotion?.Stop();
        var duration = TimeSpan.FromMilliseconds(340);
        var storyboard = new Storyboard();
        AddMotion(storyboard, SpriteHost, UIElement.OpacityProperty, 0, 1, duration);
        AddMotion(storyboard, MotionScale, ScaleTransform.ScaleXProperty, 0.91, 1, duration);
        _arrivalMotion = storyboard;
        storyboard.Begin();
    }

    private static void AddMotion(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration)
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

    private void StopMotion()
    {
        try { _idleMotion?.Stop(); } catch { }
        try { _arrivalMotion?.Stop(); } catch { }
        _idleMotion = null;
        _arrivalMotion = null;
        SpriteHost.Opacity = 1;
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
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")) // dev heuristic
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

        foreach (var k in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return k + ".png";
            yield return k + ".webp";
        }
    }
}
