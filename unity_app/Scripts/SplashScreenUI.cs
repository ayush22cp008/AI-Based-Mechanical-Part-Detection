using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Splash Screen:
/// - Dark gradient background (top #0F172A → bottom #020617)
/// - Logo + Company Name fade in smoothly (0 → 1.5 sec)
/// - Auto-navigate to Home at 2.5 sec
/// </summary>
public class SplashScreenUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign a CanvasGroup on a parent that holds BOTH Logo and CompanyName")]
    public CanvasGroup contentGroup;   // parent of Logo + CompanyName

    public float splashDuration = 1.8f; // Stay on screen for nearly 2 seconds

    void OnEnable()
    {
        // Ensure it is fully visible right away (No transparency fading)
        if (contentGroup != null) contentGroup.alpha = 1f;
        StartCoroutine(SplashRoutine());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    IEnumerator SplashRoutine()
    {
        // ── Phase 1: Wait & Display Logo──────────────────────────
        // No translucent fades; this avoids the massive "Fill-Rate" lag on mobile GPUs.
        yield return new WaitForSeconds(splashDuration);

        // ── Phase 2: Instant Seamless Jump ────────────────────
        GoHome();
    }

    void GoHome()
    {
        if (AppManager.Instance == null) return;
        AppManager.Instance.ShowHome();
    }
}
