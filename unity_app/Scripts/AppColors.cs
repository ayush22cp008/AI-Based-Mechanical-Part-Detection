using UnityEngine;

/// <summary>
/// ╔══════════════════════════════════════════════════════╗
/// ║   MECHANICAL PART DETECTOR — Central Design System   ║
/// ║   Dark Tech Theme  (#020617 + #0F172A + #22C55E)     ║
/// ╚══════════════════════════════════════════════════════╝
///
/// HOW TO USE IN CODE:
///   button.color = AppColors.Surface;          // dark panel
///   label.color  = AppColors.TextPrimary;      // light text
///   highlight    = AppColors.Accent;           // green selected state
///
/// HOW TO USE IN UNITY INSPECTOR:
///   Copy the hex values below into any Color field.
/// </summary>
public static class AppColors
{
    // ── Backgrounds ──────────────────────────────────────────────────────────
    /// <summary>#020617  Deepest dark — main screen background</summary>
    public static readonly Color Background    = HexToColor("020617");

    /// <summary>#0F172A  Secondary surface — cards, panels, default buttons</summary>
    public static readonly Color Surface       = HexToColor("0F172A");

    /// <summary>#1E293B  Raised surface — slightly lighter cards / dividers</summary>
    public static readonly Color SurfaceRaised = HexToColor("1E293B");

    // ── Text ─────────────────────────────────────────────────────────────────
    /// <summary>#E5E5E5  Primary text (headings, labels)</summary>
    public static readonly Color TextPrimary   = HexToColor("E5E5E5");

    /// <summary>#94A3B8  Secondary / muted text (captions, hints)</summary>
    public static readonly Color TextMuted     = HexToColor("94A3B8");

    // ── Accent (Detection Green) ──────────────────────────────────────────────
    /// <summary>#22C55E  Green accent — selected state, bounding boxes, CTA buttons</summary>
    public static readonly Color Accent        = HexToColor("22C55E");

    /// <summary>#16A34A  Darker green — button pressed / hover state</summary>
    public static readonly Color AccentDark    = HexToColor("16A34A");

    /// <summary>Accent with 88 % alpha — bounding-box label background</summary>
    public static readonly Color AccentLabel   = new Color(0.086f, 0.639f, 0.290f, 0.88f);

    // ── Overlays ─────────────────────────────────────────────────────────────
    /// <summary>Semi-transparent dark overlay for UI panels drawn over camera</summary>
    public static readonly Color Overlay       = new Color(0.008f, 0.024f, 0.090f, 0.80f);

    /// <summary>Fully-transparent (helper constant)</summary>
    public static readonly Color Transparent   = new Color(0f, 0f, 0f, 0f);

    // ── Utility ───────────────────────────────────────────────────────────────
    /// <summary>Converts a 6-character hex string (no #) to a Unity Color.</summary>
    public static Color HexToColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString("#" + hex, out Color c)) return c;
        Debug.LogWarning($"[AppColors] Invalid hex: #{hex}");
        return Color.magenta;
    }

    /// <summary>Returns Accent with a specific alpha (0-1).</summary>
    public static Color AccentWithAlpha(float a) =>
        new Color(Accent.r, Accent.g, Accent.b, a);

    /// <summary>Returns Surface with a specific alpha (0-1).</summary>
    public static Color SurfaceWithAlpha(float a) =>
        new Color(Surface.r, Surface.g, Surface.b, a);
}
