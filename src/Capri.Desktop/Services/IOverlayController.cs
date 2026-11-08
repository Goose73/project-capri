namespace Capri.Desktop.Services;

/// <summary>
/// Controls the compact overlay window without exposing WPF window types to callers.
/// </summary>
public interface IOverlayController
{
    /// <summary>Shows the overlay and activates input.</summary>
    void ShowOverlay();

    /// <summary>Hides the overlay.</summary>
    void HideOverlay();

    /// <summary>Toggles overlay visibility.</summary>
    void ToggleOverlay();

    /// <summary>True when the overlay is currently visible.</summary>
    bool IsOverlayVisible { get; }
}
