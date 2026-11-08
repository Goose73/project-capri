using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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

    public AvatarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Refresh();
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
        if (ReduceMotion || State == AvatarState.Sleep)
        {
            MotionTranslate.Y = State == AvatarState.Sleep ? 2 : 0;
            MotionScale.ScaleX = 1;
            MotionScale.ScaleY = 1;
            MotionRotate.Angle = State == AvatarState.Sleep ? 3 : 0;
            return;
        }

        // Avoid unnecessary animation work when not visible.
        if (!IsVisible)
        {
            MotionTranslate.Y = 0;
            MotionScale.ScaleX = 1;
            MotionScale.ScaleY = 1;
            MotionRotate.Angle = 0;
            return;
        }

        MotionRotate.Angle = 0;
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var bob = new DoubleAnimation
        {
            From = 0,
            To = State switch
            {
                AvatarState.Talking => -1.5,
                AvatarState.Happy or AvatarState.Success => -3,
                AvatarState.Surprised => -6,
                AvatarState.Annoyed or AvatarState.Error => -1,
                _ => -2
            },
            Duration = TimeSpan.FromMilliseconds(State == AvatarState.Talking ? 180 : 1400)
        };
        Storyboard.SetTarget(bob, MotionTranslate);
        Storyboard.SetTargetProperty(bob, new PropertyPath(TranslateTransform.YProperty));
        sb.Children.Add(bob);
        _idleMotion = sb;
        sb.Begin();
    }

    private void StopMotion()
    {
        try { _idleMotion?.Stop(); } catch { }
        _idleMotion = null;
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
