using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Capri.Core.Avatar;
using Capri.Core.Models;

namespace Capri.Desktop.Controls;

/// <summary>
/// Image-backed Capri compositor. State motion is a quiet background layer;
/// expressive cues briefly preempt it, with optional approved rig/pose assets.
/// No screen, microphone or desktop content is ever read by this control.
/// </summary>
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

    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _mouthTimer;
    private Storyboard? _idleMotion;
    private Storyboard? _arrivalMotion;
    private Storyboard? _cueMotion;
    private Storyboard? _blinkMotion;
    private ImageSource? _mouthOpen;
    private ImageSource? _mouthClosed;
    private bool _mouthIsOpen;
    private bool _screenFocus;
    private bool _shown;
    private int _cueRevision;
    private int _blinkCount;
    private AvatarGaze _lastGaze;

    public AvatarControl()
    {
        InitializeComponent();

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(4400)
        };
        _blinkTimer.Tick += (_, _) => BlinkOrGlance();

        _mouthTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(145)
        };
        _mouthTimer.Tick += (_, _) => AdvanceMouth();

        Loaded += (_, _) =>
        {
            Refresh();
            IfNewlyVisibleSummon();
        };
        Unloaded += (_, _) => StopMotion();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Refresh();
                IfNewlyVisibleSummon();
            }
            else
            {
                _shown = false;
                _screenFocus = false;
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

    /// <summary>Whether an optional independent transparent eye layer exists.</summary>
    public bool HasEyeRig => EyesImage.Source is not null;

    private bool CanAnimate =>
        AvatarCuePlanner.ShouldAnimate(AvatarCueKind.Summon, ReduceMotion, AvatarImage.Source is not null) &&
        IsVisible;

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AvatarControl control)
            return;

        control.Refresh();
        if (e.Property == StateProperty && !control._screenFocus)
        {
            var cue = AvatarCuePlanner.ForState(control.State);
            if (cue != AvatarCueKind.None)
                control.PlayCue(cue);
        }
    }

    private void IfNewlyVisibleSummon()
    {
        if (!IsVisible || _shown)
            return;
        _shown = true;
        PlayCue(AvatarCueKind.Summon);
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

            EyesImage.Source = LoadOptional("eyes");
            _mouthOpen = LoadOptional("mouth_open");
            _mouthClosed = LoadOptional("mouth_closed");
            BlinkPoseImage.Source = LoadOptional("blink");

            StopMotion();
            if (_screenFocus && CanAnimate)
                HoldScreenPose();
            else
                ApplyLightweightMotion();
            UpdateOptionalLayers();
        }
        catch
        {
            AvatarImage.Source = null;
            AvatarImage.Visibility = Visibility.Collapsed;
            AssetPlaceholder.Visibility = Visibility.Visible;
            StopMotion();
        }
    }

    private void StopIdleMotion()
    {
        _idleMotion?.Stop();
        _idleMotion = null;
        _blinkTimer.Stop();
        _mouthTimer.Stop();
    }

    private void ResetTransforms()
    {
        MotionTranslate.X = 0;
        MotionTranslate.Y = 0;
        MotionScale.ScaleX = 1;
        MotionScale.ScaleY = 1;
        MotionRotate.Angle = 0;
    }

    private void ApplyLightweightMotion()
    {
        StopIdleMotion();
        ResetTransforms();
        if (!CanAnimate || _screenFocus)
            return;

        var profile = AvatarMotionPlanner.ForState(State, ReduceMotion);
        if (!profile.Animated)
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
        _blinkTimer.Start();
        UpdateOptionalLayers();
    }

    /// <summary>
    /// Plays an attention/gesture cue. ScreenRequested means an attempt to turn
    /// toward the display, NOT that a screenshot or screen inspection occurred.
    /// </summary>
    public void PlayCue(AvatarCueKind cue)
    {
        if (!AvatarCuePlanner.ShouldAnimate(cue, ReduceMotion, AvatarImage.Source is not null) ||
            !IsVisible)
        {
            if (cue == AvatarCueKind.ReturnToUser)
                _screenFocus = false;
            ResetGaze();
            UpdateOptionalLayers();
            return;
        }

        if (cue == AvatarCueKind.Summon)
        {
            PlayArrivalMotion();
            return;
        }

        if (_screenFocus && cue is not (AvatarCueKind.ScreenRequested or AvatarCueKind.ReturnToUser))
            return;

        if (cue == AvatarCueKind.ScreenRequested)
            _screenFocus = true;
        else if (cue == AvatarCueKind.ReturnToUser)
            _screenFocus = false;

        StopIdleMotion();
        _arrivalMotion?.Stop();
        _arrivalMotion = null;
        _cueMotion?.Stop();
        _cueMotion = null;
        _cueRevision++;
        var revision = _cueRevision;
        ResetTransforms();

        if (cue == AvatarCueKind.ScreenRequested)
            SetCuePose("turn_away", "turned_away", "back", "screen");
        else if (cue == AvatarCueKind.ReturnToUser)
            SetCuePose("turn_away", "turned_away", "back", "screen");
        else
            SetCuePose(PoseKeyFor(cue));

        var board = new Storyboard { FillBehavior = FillBehavior.Stop };
        BuildCueTimeline(board, cue);

        _cueMotion = board;
        board.Completed += (_, _) =>
        {
            if (_cueRevision != revision)
                return;
            board.Stop();
            _cueMotion = null;
            ResetTransforms();
            if (_screenFocus)
                HoldScreenPose();
            else
            {
                ClearCuePose();
                ApplyLightweightMotion();
            }
            UpdateOptionalLayers();
        };
        UpdateOptionalLayers();
        board.Begin();
    }

    /// <summary>
    /// Point-gaze is limited to a few pixels and only uses the independent
    /// eye rig if present; otherwise Capri makes a small head-like lean.
    /// No cursor coordinates are stored or transmitted.
    /// </summary>
    public void SetGaze(double x, double y)
    {
        var gaze = AvatarCuePlanner.LimitGaze(x, y, ReduceMotion);
        if (!CanAnimate || _screenFocus)
            gaze = default;
        if (Math.Abs(gaze.X - _lastGaze.X) < 0.035 &&
            Math.Abs(gaze.Y - _lastGaze.Y) < 0.035)
            return;

        _lastGaze = gaze;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(130);
        if (HasEyeRig)
        {
            AnimateValue(EyeTranslate, TranslateTransform.XProperty, gaze.X * 3.2, duration, ease);
            AnimateValue(EyeTranslate, TranslateTransform.YProperty, gaze.Y * 2.2, duration, ease);
            AnimateValue(GazeHeadRotate, RotateTransform.AngleProperty, 0, duration, ease);
        }
        else
        {
            AnimateValue(EyeTranslate, TranslateTransform.XProperty, 0, duration, ease);
            AnimateValue(EyeTranslate, TranslateTransform.YProperty, 0, duration, ease);
            AnimateValue(GazeHeadRotate, RotateTransform.AngleProperty,
                gaze.X * 1.2, duration, ease);
        }
    }

    public void ResetGaze()
    {
        _lastGaze = new AvatarGaze(double.NaN, double.NaN);
        SetGaze(0, 0);
    }

    private void HoldScreenPose()
    {
        ResetTransforms();
        MotionTranslate.X = 2.5;
        MotionRotate.Angle = 5;
        MotionScale.ScaleX = 0.96;
        SetCuePose("turn_away", "turned_away", "back", "screen");
        CuePoseImage.Opacity = 1;
        UpdateOptionalLayers();
    }

    private void PlayArrivalMotion()
    {
        StopIdleMotion();
        _cueMotion?.Stop();
        _cueMotion = null;
        _cueRevision++;
        if (!CanAnimate)
        {
            SpriteHost.Opacity = 1;
            return;
        }

        _arrivalMotion?.Stop();
        var revision = _cueRevision;
        var board = new Storyboard { FillBehavior = FillBehavior.Stop };
        AddKeys(board, SpriteHost, UIElement.OpacityProperty,
            (0, 0), (65, 0.1), (175, 0.88), (370, 1));
        AddKeys(board, MotionTranslate, TranslateTransform.YProperty,
            (0, 25), (150, -6), (370, 0));
        AddKeys(board, MotionScale, ScaleTransform.ScaleXProperty,
            (0, 0.70), (125, 1.05), (370, 1));
        AddKeys(board, MotionScale, ScaleTransform.ScaleYProperty,
            (0, 0.60), (150, 1.03), (370, 1));
        AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
            (0, -9), (155, 3.0), (370, 0));
        _arrivalMotion = board;
        board.Completed += (_, _) =>
        {
            if (_cueRevision != revision) return;
            board.Stop();
            _arrivalMotion = null;
            SpriteHost.Opacity = 1;
            ApplyLightweightMotion();
        };
        board.Begin();
    }

    private void BlinkOrGlance()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(++_blinkCount % 3 == 0 ? 5900 : 4300);
        if (!CanAnimate || _screenFocus || _cueMotion is not null)
            return;

        if (BlinkPoseImage.Source is not null &&
            State is AvatarState.Idle or AvatarState.Listening or AvatarState.Thinking)
        {
            _blinkMotion?.Stop();
            BlinkPoseImage.Opacity = 0;
            BlinkPoseImage.Visibility = Visibility.Visible;
            var board = new Storyboard { FillBehavior = FillBehavior.Stop };
            AddKeys(board, BlinkPoseImage, UIElement.OpacityProperty,
                (0, 0), (55, 1), (115, 1), (190, 0));
            _blinkMotion = board;
            board.Completed += (_, _) =>
            {
                board.Stop();
                BlinkPoseImage.Visibility = Visibility.Collapsed;
                _blinkMotion = null;
            };
            board.Begin();
        }
        else if (State == AvatarState.Idle && _blinkCount % 2 == 0)
        {
            PlayCue(_blinkCount % 4 == 0 ? AvatarCueKind.GlanceLeft : AvatarCueKind.GlanceRight);
        }
    }

    private void AdvanceMouth()
    {
        if (!CanAnimate || State != AvatarState.Talking || _screenFocus || _mouthOpen is null)
        {
            MouthImage.Visibility = Visibility.Collapsed;
            _mouthTimer.Stop();
            return;
        }

        _mouthIsOpen = !_mouthIsOpen;
        MouthImage.Source = _mouthIsOpen ? _mouthOpen : _mouthClosed;
        MouthImage.Visibility = MouthImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateOptionalLayers()
    {
        if (!CanAnimate || _screenFocus)
        {
            _mouthTimer.Stop();
            EyesImage.Visibility = Visibility.Collapsed;
            MouthImage.Visibility = Visibility.Collapsed;
            BlinkPoseImage.Visibility = Visibility.Collapsed;
            return;
        }

        var faceAligned = State is AvatarState.Idle or AvatarState.Listening or
            AvatarState.Thinking or AvatarState.Talking;
        EyesImage.Visibility = faceAligned && HasEyeRig && CuePoseImage.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        if (State == AvatarState.Talking && _mouthOpen is not null &&
            CuePoseImage.Visibility != Visibility.Visible)
        {
            if (!_mouthTimer.IsEnabled) _mouthTimer.Start();
        }
        else
        {
            _mouthTimer.Stop();
            MouthImage.Visibility = Visibility.Collapsed;
        }
    }

    private void SetCuePose(params string?[] names)
    {
        CuePoseImage.Source = LoadOptional(names.Where(n => n is not null).Select(n => n!).ToArray());
        CuePoseImage.Visibility = CuePoseImage.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        CuePoseImage.Opacity = 1;
    }

    private void ClearCuePose()
    {
        CuePoseImage.Visibility = Visibility.Collapsed;
        CuePoseImage.Source = null;
        CuePoseImage.Opacity = 1;
    }

    private static string? PoseKeyFor(AvatarCueKind cue) => cue switch
    {
        AvatarCueKind.Nod => "nod",
        AvatarCueKind.Shake => "shake",
        AvatarCueKind.Hop => "hop",
        AvatarCueKind.LeanIn => "lean_in",
        AvatarCueKind.StepLeft => "step_left",
        AvatarCueKind.StepRight => "step_right",
        AvatarCueKind.Wink => "wink",
        AvatarCueKind.SpeakEmphasis => "talking",
        AvatarCueKind.GlanceLeft => "look_left",
        AvatarCueKind.GlanceRight => "look_right",
        AvatarCueKind.LookAround => "look_around",
        _ => null
    };

    private static void BuildCueTimeline(Storyboard board, AvatarCueKind cue)
    {
        switch (cue)
        {
            case AvatarCueKind.ScreenRequested:
                AddKeys(board, MotionScale, ScaleTransform.ScaleXProperty,
                    (0, 1), (105, 0.42), (200, 1), (360, 0.96));
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (130, -7), (275, 8), (360, 5));
                AddKeys(board, MotionTranslate, TranslateTransform.XProperty,
                    (0, 0), (130, -4), (360, 2.5));
                AddKeys(board, CuePoseImage, UIElement.OpacityProperty,
                    (0, 0), (150, 0), (210, 1), (360, 1));
                break;
            case AvatarCueKind.ReturnToUser:
                AddKeys(board, MotionScale, ScaleTransform.ScaleXProperty,
                    (0, 0.96), (135, 0.44), (300, 1));
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 5), (135, -5), (300, 0));
                AddKeys(board, CuePoseImage, UIElement.OpacityProperty,
                    (0, 1), (130, 1), (210, 0), (300, 0));
                break;
            case AvatarCueKind.Nod or AvatarCueKind.SpeakEmphasis:
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (100, 4), (175, -1.5), (275, 0));
                AddKeys(board, MotionTranslate, TranslateTransform.YProperty,
                    (0, 0), (110, 2.5), (265, 0));
                break;
            case AvatarCueKind.Shake:
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (85, -4.5), (185, 4.5), (280, -3.5), (380, 0));
                AddKeys(board, MotionTranslate, TranslateTransform.XProperty,
                    (0, 0), (100, -3), (205, 3), (380, 0));
                break;
            case AvatarCueKind.Hop:
                AddKeys(board, MotionTranslate, TranslateTransform.YProperty,
                    (0, 0), (100, 3), (185, -11), (285, -13), (440, 0));
                AddKeys(board, MotionScale, ScaleTransform.ScaleYProperty,
                    (0, 1), (100, 0.93), (230, 1.03), (440, 1));
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (185, -3), (440, 0));
                break;
            case AvatarCueKind.LeanIn:
                AddKeys(board, MotionScale, ScaleTransform.ScaleYProperty,
                    (0, 1), (150, 1.034), (330, 1));
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (110, -3.5), (330, 0));
                break;
            case AvatarCueKind.StepLeft or AvatarCueKind.StepRight:
                var sign = cue == AvatarCueKind.StepLeft ? -1 : 1;
                AddKeys(board, MotionTranslate, TranslateTransform.XProperty,
                    (0, 0), (120, sign * 9), (255, sign * 14), (470, 0));
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (170, sign * 4), (470, 0));
                break;
            case AvatarCueKind.GlanceLeft or AvatarCueKind.GlanceRight:
                var direction = cue == AvatarCueKind.GlanceLeft ? -1 : 1;
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (85, direction * 3), (325, direction * 3), (430, 0));
                break;
            case AvatarCueKind.LookAround:
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (85, -3), (230, -3), (315, 3), (500, 3), (590, 0));
                AddKeys(board, MotionTranslate, TranslateTransform.XProperty,
                    (0, 0), (190, -2), (465, 2), (590, 0));
                break;
            case AvatarCueKind.Wink or AvatarCueKind.Blink:
                AddKeys(board, MotionScale, ScaleTransform.ScaleYProperty,
                    (0, 1), (90, 0.98), (200, 1));
                break;
            default:
                AddKeys(board, MotionRotate, RotateTransform.AngleProperty,
                    (0, 0), (180, 2.5), (360, 0));
                break;
        }
    }

    private static void AddKeys(
        Storyboard board, DependencyObject target, DependencyProperty property,
        params (int Ms, double Value)[] points)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, value) in points)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        }

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        board.Children.Add(animation);
    }

    private static void AddMotion(
        Storyboard board, DependencyObject target, DependencyProperty property,
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
        board.Children.Add(animation);
    }

    private static void AnimateValue(
        Animatable target, DependencyProperty property,
        double to, TimeSpan duration, IEasingFunction easing)
    {
        target.BeginAnimation(property, new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = easing
        });
    }

    private void StopMotion()
    {
        _cueRevision++;
        StopIdleMotion();
        _cueMotion?.Stop();
        _cueMotion = null;
        _arrivalMotion?.Stop();
        _arrivalMotion = null;
        _blinkMotion?.Stop();
        _blinkMotion = null;
        BlinkPoseImage.Visibility = Visibility.Collapsed;
        ClearCuePose();
        SpriteHost.Opacity = 1;
        ResetTransforms();
        EyeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        EyeTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        GazeHeadRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        EyeTranslate.X = 0;
        EyeTranslate.Y = 0;
        GazeHeadRotate.Angle = 0;
        _lastGaze = default;
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

    private static ImageSource? LoadOptional(params string[] names)
    {
        foreach (var root in GetCandidateRoots())
        {
            var dir = Path.Combine(root, "design", "assets", "capri");
            if (!Directory.Exists(dir)) continue;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                foreach (var extension in new[] { ".png", ".webp" })
                {
                    var file = Path.Combine(dir, name + extension);
                    if (File.Exists(file)) return LoadImage(file);
                }
            }
        }

        return null;
    }

    private static string? FindAssetPath(AvatarState state)
    {
        foreach (var root in GetCandidateRoots())
        {
            var dir = Path.Combine(root, "design", "assets", "capri");
            if (!Directory.Exists(dir)) continue;
            foreach (var name in GetCandidateNames(state))
            {
                var file = Path.Combine(dir, name);
                if (File.Exists(file)) return file;
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
