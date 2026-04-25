using UnityEngine;

/// <summary>
/// DetectionValidator — filters raw YOLO detections before they enter tracking.
///
/// Rejects any detection where:
///   1. The bounding box is too close to any screen edge (partial / cut object).
///   2. The bounding box is too small (distant / incomplete object).
///   3. The aspect ratio is unrealistic (distorted or occluded object).
///   4. Confidence is below the hard minimum threshold.
///
/// All thresholds are tunable in the Inspector so you can dial them without
/// recompiling.  The class is pure-static-friendly (no MonoBehaviour state used
/// in the validation path) so it is extremely fast.
/// </summary>
public class DetectionValidator : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Edge / Margin Filter")]
    [Tooltip("Minimum fractional distance a box edge must be from the screen border (0–0.5). " +
             "e.g. 0.05 = 5% of screen width/height. Keeps cut objects out.")]
    [Range(0f, 0.15f)]
    public float edgeMargin = 0.05f;

    [Header("Size Filter")]
    [Tooltip("Minimum width of bounding box as fraction of screen width (0–1).")]
    [Range(0f, 0.5f)]
    public float minWidthFraction = 0.04f;

    [Tooltip("Minimum height of bounding box as fraction of screen height (0–1).")]
    [Range(0f, 0.5f)]
    public float minHeightFraction = 0.04f;

    [Tooltip("Maximum width of bounding box as fraction of screen width (0–1). " +
             "Rejects boxes that fill the entire view (usually noise).")]
    [Range(0.5f, 1f)]
    public float maxWidthFraction = 0.92f;

    [Tooltip("Maximum height of bounding box as fraction of screen height (0–1).")]
    [Range(0.5f, 1f)]
    public float maxHeightFraction = 0.92f;

    [Header("Aspect Ratio Filter")]
    [Tooltip("Minimum width/height ratio allowed. Rejects extremely thin horizontal strips.")]
    [Range(0.05f, 1f)]
    public float minAspectRatio = 0.1f;

    [Tooltip("Maximum width/height ratio allowed. Rejects extremely wide/flat boxes.")]
    [Range(1f, 20f)]
    public float maxAspectRatio = 10f;

    [Header("Confidence Filter")]
    [Tooltip("Hard minimum confidence required before any other filter is applied.")]
    [Range(0.1f, 1f)]
    public float minConfidence = 0.45f;

    [Header("Debug")]
    public bool debugLogs = false;

    // ─── Public API ───────────────────────────────────────────────────────────
    /// <summary>
    /// Returns true if the detection passes ALL validation rules and should be
    /// forwarded to the tracker.  Returns false (and optionally logs the reason)
    /// if the detection should be silently discarded.
    /// </summary>
    /// <param name="box">Normalised bounding box (x,y,w,h) in 0–1 UV space.</param>
    /// <param name="confidence">YOLO confidence score (0–1).</param>
    public bool IsValid(Rect box, float confidence)
    {
        // ── 1. Confidence ──────────────────────────────────────────────────
        if (confidence < minConfidence)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED low-conf {confidence:F2}");
            return false;
        }

        // ── 2. Edge margin ────────────────────────────────────────────────
        // Box must not touch or cross the forbidden border zone on any side.
        if (box.x < edgeMargin)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED left-edge x={box.x:F3}");
            return false;
        }
        if (box.y < edgeMargin)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED top-edge y={box.y:F3}");
            return false;
        }
        if (box.xMax > 1f - edgeMargin)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED right-edge xMax={box.xMax:F3}");
            return false;
        }
        if (box.yMax > 1f - edgeMargin)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED bottom-edge yMax={box.yMax:F3}");
            return false;
        }

        // ── 3. Minimum size ───────────────────────────────────────────────
        if (box.width < minWidthFraction)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED too-narrow w={box.width:F3}");
            return false;
        }
        if (box.height < minHeightFraction)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED too-short h={box.height:F3}");
            return false;
        }

        // ── 4. Maximum size ───────────────────────────────────────────────
        if (box.width > maxWidthFraction)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED too-wide w={box.width:F3}");
            return false;
        }
        if (box.height > maxHeightFraction)
        {
            if (debugLogs) Debug.Log($"[Validator] REJECTED too-tall h={box.height:F3}");
            return false;
        }

        // ── 5. Aspect ratio ───────────────────────────────────────────────
        if (box.height > 0f)
        {
            float ar = box.width / box.height;
            if (ar < minAspectRatio)
            {
                if (debugLogs) Debug.Log($"[Validator] REJECTED aspect too-narrow ar={ar:F2}");
                return false;
            }
            if (ar > maxAspectRatio)
            {
                if (debugLogs) Debug.Log($"[Validator] REJECTED aspect too-wide ar={ar:F2}");
                return false;
            }
        }

        return true; // ✅ All checks passed
    }
}
