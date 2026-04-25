using UnityEngine;

/// <summary>
/// Forces the app into Portrait orientation before ANY scene or script loads.
/// This runs even before Awake() — guaranteeing portrait at all times.
/// </summary>
public static class ForcePortrait
{
    // RuntimeInitializeOnLoadMethod fires as soon as the Unity runtime starts.
    // BeforeSceneLoad = before the first scene is even loaded.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        // Disable ALL auto-rotation
        Screen.autorotateToPortrait           = true;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft      = false;
        Screen.autorotateToLandscapeRight     = false;

        // Force portrait immediately
        Screen.orientation = ScreenOrientation.Portrait;

        Debug.Log("[ForcePortrait] Screen locked to Portrait before scene load.");
    }
}
