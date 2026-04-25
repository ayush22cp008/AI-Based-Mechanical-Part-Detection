using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a vertical gradient on a UI Image (standard Panel component).
/// Works on ANY Panel — no RawImage needed.
///
/// Default colors (AppColors design system):
///   Top    → #0F172A  (dark navy)
///   Bottom → #020617  (near black)
///
/// HOW TO USE:
///   1. Select your Panel in the Hierarchy
///   2. Add Component → UIGradientBackground
///   Done — gradient applies automatically on Play.
/// </summary>
[RequireComponent(typeof(Image))]
public class UIGradientBackground : MonoBehaviour
{
    [Header("Gradient Colors (app dark theme defaults)")]
    public Color topColor    = new Color(0.059f, 0.090f, 0.165f, 1f);  // #0F172A
    public Color bottomColor = new Color(0.008f, 0.024f, 0.090f, 1f);  // #020617

    void Start() => ApplyGradient();

    /// <summary>Call this at runtime if you change topColor / bottomColor dynamically.</summary>
    public void ApplyGradient()
    {
        // Build a 1×256 Texture2D gradient
        var tex = new Texture2D(1, 256, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            float t  = (float)i / 255f;   // 0 = bottom → 1 = top
            pixels[i] = Color.Lerp(bottomColor, topColor, t);
        }
        tex.SetPixels(pixels);
        tex.Apply();

        // Convert to Sprite and assign to the Image component
        var sprite = Sprite.Create(
            tex,
            new Rect(0, 0, 1, 256),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect
        );

        var img = GetComponent<Image>();
        img.sprite = sprite;
        img.type   = Image.Type.Simple;
        img.color  = Color.white;  // must be white so the sprite colors show through
    }
}
