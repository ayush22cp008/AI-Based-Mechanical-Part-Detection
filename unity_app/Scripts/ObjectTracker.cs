using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ObjectTracker — owns the lifecycle of every detected object.
///
/// Responsibilities:
///   • Receive validated detections from YoloSentisDetector.
///   • Match detections to existing tracked objects (IoU + distance + class).
///   • Manage TRACKED → LOST → RE-DETECT → NEW state machine.
///   • Maintain a pooled set of UI bounding-box widgets.
///   • Update widget positions every frame using smooth momentum-based lerp.
///   • Expose UIAlpha so MotionGate can dim all boxes during fast movement.
///
/// ObjectTracker intentionally has NO knowledge of the YOLO model — it only
/// works with the abstract Detection struct that the detector feeds it.
/// </summary>
public class ObjectTracker : MonoBehaviour
{
    // ─── Public types ─────────────────────────────────────────────────────────
    public struct Detection
    {
        public Rect  Box;
        public int   ClassId;
        public float Confidence;
    }

    public enum ObjectState { New, Tracked, Lost, ReDetect }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("UI References")]
    [Tooltip("Parent RectTransform that all bounding-box widgets are spawned inside.")]
    public RectTransform boxContainer;

    [Tooltip("Optional: MotionGate component. When assigned, UI alpha is multiplied by MotionGate.UIAlpha.")]
    public MotionGate motionGate;

    [Header("Tracking Thresholds")]
    [Tooltip("IoU below this = not same object.")]
    [Range(0f, 0.5f)]
    public float iouMatchThreshold = 0.1f;

    [Tooltip("Center distance (normalised 0-1) below this = potential match.")]
    [Range(0f, 0.5f)]
    public float distMatchThreshold = 0.15f;

    [Tooltip("Number of consecutive missed inferences before moving to LOST.")]
    [Range(1, 10)]
    public int missedToLost = 2;

    [Tooltip("Number of consecutive missed inferences while LOST before the object is destroyed.")]
    [Range(1, 30)]
    public int missedToDestroy = 8;

    [Header("Smoothing")]
    [Tooltip("Box lerp speed (exponential). Higher = snappier, lower = smoother.")]
    [Range(1f, 60f)]
    public float boxSmoothSpeed = 22f;

    [Tooltip("Max momentum prediction window (seconds). Keeps box from drifting too far.")]
    [Range(0f, 0.5f)]
    public float maxPredictionTime = 0.30f;

    [Header("Limits")]
    [Range(1, 10)]
    public int maxTrackedObjects = 5;

    [Header("Label Filter")]
    [Tooltip("If non-empty, only boxes matching this class name are shown (case-insensitive).")]
    public string filterLabel = "";

    [Header("Debug")]
    public bool debugLogs = false;

    // ─── Public API ───────────────────────────────────────────────────────────
    /// <summary>True if at least one box matching filterLabel is currently visible.</summary>
    public bool IsFilteredObjectVisible { get; private set; }

    /// <summary>Number of objects currently in TRACKED or NEW state (visible on screen).</summary>
    public int TrackedCount { get; private set; }

    // ─── Private: tracked object data ─────────────────────────────────────────
    private class TrackedObject
    {
        public int         Id;
        public ObjectState State;

        // Detection data
        public Rect  TargetBox;       // Last known YOLO position (normalised 0-1)
        public Rect  CurrentBox;      // Smoothed render position
        public Vector2 Velocity;      // Pixels/s momentum (for prediction)
        public int   ClassId;
        public float Confidence;

        // Lifecycle
        public int   MissedFrames;
        public float LastDetectionTime;

        // UI — pooled widget refs
        public GameObject           UI;
        public RectTransform        RT;
        public CanvasGroup          CG;
        public Image[]              Borders;
        public TMPro.TextMeshProUGUI LabelText;
        public TMPro.TextMeshProUGUI ScoreText;

        // Dirty flags → skip TMP string rebuild when nothing changed
        public int   LastClassId  = -1;
        public int   LastConfInt  = -1;
    }

    private readonly List<TrackedObject> _tracked = new List<TrackedObject>();
    private readonly List<TrackedObject> _pool    = new List<TrackedObject>();

    private static int _nextId = 0;

    // Scratch list — reused every frame to avoid allocation
    private readonly List<Detection> _unassigned = new List<Detection>();

    // Letterbox padding (set by detector each inference frame)
    private float _padXNorm, _padYNorm;

    // Class name table — set once by detector
    private string[]   _classNames;
    private string[]   _classNamesUpper;
    private static Color[] _palette;

    // ─────────────────────────────────────────────────────────────────────────
    // Init ────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Called by YoloSentisDetector once class names are loaded.</summary>
    public void SetClassNames(string[] names)
    {
        _classNames      = names;
        _classNamesUpper = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
            _classNamesUpper[i] = names[i].ToUpper();

        _palette = GeneratePalette(names.Length);
    }

    /// <summary>Called by YoloSentisDetector each inference to pass letterbox padding.</summary>
    public void SetLetterboxPadding(float padXNorm, float padYNorm)
    {
        _padXNorm = padXNorm;
        _padYNorm = padYNorm;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // New Detection Feed ──────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feed a fresh batch of validated detections.  Call this from the detector's
    /// Update() whenever a new inference result arrives.
    /// </summary>
    public void FeedDetections(List<Detection> detections)
    {
        if (_classNames == null) return;

        // ── 1. Prepare unassigned list ────────────────────────────────────
        _unassigned.Clear();
        _unassigned.AddRange(detections);

        // ── 2. Increment missed frames for all tracked objects ─────────────
        foreach (var t in _tracked) t.MissedFrames++;

        // ── 3. Match detections → tracked objects ─────────────────────────
        foreach (var t in _tracked)
        {
            int   bestIdx   = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < _unassigned.Count; i++)
            {
                float iou  = CalcIoU(t.TargetBox, _unassigned[i].Box);
                float dist = Vector2.Distance(t.TargetBox.center, _unassigned[i].Box.center);

                // Gate: must overlap meaningfully OR be close enough
                if (iou < iouMatchThreshold && dist >= distMatchThreshold) continue;

                // Blended score: IoU weighted heavily, proximity bonus, class bonus
                float classBonus = (t.ClassId == _unassigned[i].ClassId) ? 0.15f : 0f;
                float score      = iou * 0.7f + Mathf.Max(0f, distMatchThreshold - dist) * 2f + classBonus;

                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            if (bestIdx >= 0)
            {
                // ── Matched: update tracking data ──────────────────────────
                var  det       = _unassigned[bestIdx];
                float timeDelta = Time.time - t.LastDetectionTime;

                if (timeDelta > 0.001f && timeDelta < 1.0f)
                {
                    Vector2 newVel = (det.Box.position - t.TargetBox.position) / timeDelta;
                    t.Velocity = Vector2.Lerp(t.Velocity, newVel, 0.4f);
                }
                else
                    t.Velocity = Vector2.zero;

                t.TargetBox         = det.Box;
                t.ClassId           = det.ClassId;
                t.Confidence        = det.Confidence;
                t.MissedFrames      = 0;
                t.LastDetectionTime = Time.time;
                t.State             = ObjectState.Tracked;

                _unassigned.RemoveAt(bestIdx);
            }
        }

        // ── 4. Handle state transitions ───────────────────────────────────
        foreach (var t in _tracked)
        {
            if (t.MissedFrames == 0) continue; // Already updated above

            if (t.MissedFrames >= missedToLost && t.State == ObjectState.Tracked)
            {
                t.State = ObjectState.Lost;
                t.Velocity = Vector2.zero;
                if (debugLogs) Debug.Log($"[Tracker] Object {t.Id} → LOST");
            }
            if (t.MissedFrames >= missedToLost + 2 && t.State == ObjectState.Lost)
            {
                t.State = ObjectState.ReDetect;
                if (debugLogs) Debug.Log($"[Tracker] Object {t.Id} → RE-DETECT");
            }
        }

        // ── 5. Add new objects for unmatched detections ───────────────────
        foreach (var det in _unassigned)
        {
            if (_tracked.Count >= maxTrackedObjects) break;

            TrackedObject to = GetPooledObject();
            to.Id                = _nextId++;
            to.State             = ObjectState.New;
            to.TargetBox         = det.Box;
            to.CurrentBox        = det.Box;
            to.ClassId           = det.ClassId;
            to.Confidence        = det.Confidence;
            to.MissedFrames      = 0;
            to.LastDetectionTime = Time.time;
            to.Velocity          = Vector2.zero;
            to.LastClassId       = -1;
            to.LastConfInt       = -1;
            to.CG.alpha          = 0f;
            to.UI.SetActive(true);
            _tracked.Add(to);

            if (debugLogs) Debug.Log($"[Tracker] NEW object {to.Id} class={_classNames[det.ClassId]}");
        }

        // ── 6. Destroy objects that have been lost too long ───────────────
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            if (_tracked[i].MissedFrames > missedToDestroy)
            {
                if (debugLogs) Debug.Log($"[Tracker] Removing object {_tracked[i].Id}");
                ReturnToPool(_tracked[i]);
                _tracked.RemoveAt(i);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-Frame UI Update ─────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call every frame from YoloSentisDetector.Update() — updates all box
    /// positions, alphas, and labels.
    /// </summary>
    public void UpdateUI()
    {
        if (boxContainer == null || _classNames == null) return;

        float gateAlpha = (motionGate != null) ? motionGate.UIAlpha : 1f;
        bool  filteredVisible = false;

        foreach (var t in _tracked)
        {
            // ── Filter by label ───────────────────────────────────────────
            bool matchesFilter = string.IsNullOrEmpty(filterLabel) ||
                                 _classNames[t.ClassId].Equals(filterLabel,
                                     System.StringComparison.OrdinalIgnoreCase);

            if (!matchesFilter)
            {
                t.CG.alpha = 0f;
                continue;
            }

            // ── Determine per-box visibility based on state ───────────────
            float targetAlpha;
            switch (t.State)
            {
                case ObjectState.New:
                    t.State    = ObjectState.Tracked;
                    targetAlpha = 1f;
                    break;
                case ObjectState.Tracked:
                    targetAlpha = (t.MissedFrames == 0) ? 1f : 0f;
                    filteredVisible = true;
                    break;
                case ObjectState.Lost:
                    targetAlpha = 0f;
                    break;
                case ObjectState.ReDetect:
                    targetAlpha = 0f;
                    break;
                default:
                    targetAlpha = 0f;
                    break;
            }

            // Multiply by MotionGate alpha (hides everything during fast movement)
            float finalAlpha = targetAlpha * gateAlpha;
            t.CG.alpha = Mathf.MoveTowards(t.CG.alpha, finalAlpha, Time.deltaTime * 14f);

            if (t.CG.alpha > 0.01f) filteredVisible = true;

            // ── Predictive momentum smoothing ─────────────────────────────
            float drift        = Mathf.Clamp(Time.time - t.LastDetectionTime, 0f, maxPredictionTime);
            Rect  ghostTarget  = new Rect(
                t.TargetBox.x + t.Velocity.x * drift,
                t.TargetBox.y + t.Velocity.y * drift,
                t.TargetBox.width,
                t.TargetBox.height);

            float lerpT = 1f - Mathf.Exp(-boxSmoothSpeed * Time.deltaTime);
            t.CurrentBox = new Rect(
                Mathf.Lerp(t.CurrentBox.x, ghostTarget.x, lerpT),
                Mathf.Lerp(t.CurrentBox.y, ghostTarget.y, lerpT),
                Mathf.Lerp(t.CurrentBox.width,  ghostTarget.width,  lerpT),
                Mathf.Lerp(t.CurrentBox.height, ghostTarget.height, lerpT));

            // ── Letterbox un-pad + anchor mapping ─────────────────────────
            float scaleX   = 1f - 2f * _padXNorm;
            float scaleY   = 1f - 2f * _padYNorm;
            float safeScX  = Mathf.Max(scaleX, 0.001f);
            float safeScY  = Mathf.Max(scaleY, 0.001f);

            float xN = (t.CurrentBox.x - _padXNorm) / safeScX;
            float yN = (t.CurrentBox.y - _padYNorm) / safeScY;
            float wN = t.CurrentBox.width  / safeScX;
            float hN = t.CurrentBox.height / safeScY;

            xN = Mathf.Clamp01(xN);
            yN = Mathf.Clamp01(yN);
            wN = Mathf.Clamp(wN, 0f, 1f - xN);
            hN = Mathf.Clamp(hN, 0f, 1f - yN);

            t.RT.anchorMin = new Vector2(xN,       1f - yN - hN);
            t.RT.anchorMax = new Vector2(xN + wN,  1f - yN);
            t.RT.offsetMin = Vector2.zero;
            t.RT.offsetMax = Vector2.zero;

            // ── Label / colour update (dirty-flag guarded) ────────────────
            if (t.LastClassId != t.ClassId)
            {
                Color col = _palette[t.ClassId % _palette.Length];
                foreach (var b in t.Borders) b.color = col;
                t.LabelText.text = _classNamesUpper[t.ClassId];
                t.LastClassId    = t.ClassId;
            }

            int confInt = Mathf.RoundToInt(t.Confidence * 100);
            if (t.LastConfInt != confInt)
            {
                t.ScoreText.text = $"{confInt}%";
                t.LastConfInt    = confInt;
            }
        }

        IsFilteredObjectVisible = filteredVisible;

        // Count objects currently in NEW or TRACKED state (visible / being tracked)
        int count = 0;
        foreach (var t in _tracked)
            if (t.State == ObjectState.New || t.State == ObjectState.Tracked) count++;
        TrackedCount = count;
    }

    /// <summary>Query whether any object is in RE-DETECT state (triggers YOLO re-run).</summary>
    public bool NeedsReDetection()
    {
        foreach (var t in _tracked)
            if (t.State == ObjectState.ReDetect) return true;
        return false;
    }

    /// <summary>Immediately hide all boxes (called on screen disable / transition).</summary>
    public void ClearAll()
    {
        foreach (var t in _tracked) ReturnToPool(t);
        _tracked.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Object Pooling ──────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    private TrackedObject GetPooledObject()
    {
        foreach (var p in _pool)
            if (!p.UI.activeSelf) return p;

        var created = CreateWidget();
        _pool.Add(created);
        return created;
    }

    private void ReturnToPool(TrackedObject t)
    {
        t.UI.SetActive(false);
        t.State = ObjectState.Lost;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI Widget Factory ───────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    private TrackedObject CreateWidget()
    {
        // Root
        GameObject root = new GameObject("DetectionWidget");
        root.transform.SetParent(boxContainer, false);
        var rootRT = root.AddComponent<RectTransform>();
        var cg     = root.AddComponent<CanvasGroup>();

        // Border container
        GameObject border = new GameObject("Border");
        border.transform.SetParent(root.transform, false);
        var bRT = border.AddComponent<RectTransform>();
        bRT.anchorMin = Vector2.zero;
        bRT.anchorMax = Vector2.one;
        bRT.offsetMin = Vector2.zero;
        bRT.offsetMax = Vector2.zero;

        const int th = 8; // border thickness px
        Image top    = MakeLine(border.transform, "Top",    new Vector2(0,1), new Vector2(1,1), new Vector2(-th,-th), new Vector2(th, 0));
        Image bottom = MakeLine(border.transform, "Bottom", new Vector2(0,0), new Vector2(1,0), new Vector2(-th, 0),  new Vector2(th,th));
        Image left   = MakeLine(border.transform, "Left",   new Vector2(0,0), new Vector2(0,1), new Vector2(0,   0),  new Vector2(th, 0));
        Image right  = MakeLine(border.transform, "Right",  new Vector2(1,0), new Vector2(1,1), new Vector2(-th, 0),  Vector2.zero);

        // Label background
        GameObject bg   = new GameObject("LabelBg");
        bg.transform.SetParent(root.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin      = new Vector2(0.5f, 1f);
        bgRT.anchorMax      = new Vector2(0.5f, 1f);
        bgRT.pivot          = new Vector2(0.5f, 0f);
        bgRT.anchoredPosition = new Vector2(0f, 10f);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);

        var layout = bg.AddComponent<HorizontalLayoutGroup>();
        layout.childControlWidth  = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;
        layout.padding  = new RectOffset(18, 18, 8, 8);
        layout.spacing  = 12;
        layout.childAlignment = TextAnchor.MiddleCenter;

        var fitter = bg.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Label text
        GameObject lObj   = new GameObject("Label");
        lObj.transform.SetParent(bg.transform, false);
        var labelTxt = lObj.AddComponent<TMPro.TextMeshProUGUI>();
        labelTxt.fontSize  = 28;
        labelTxt.color     = Color.white;
        labelTxt.fontStyle = TMPro.FontStyles.Bold;
        labelTxt.alignment = TMPro.TextAlignmentOptions.Center;

        // Score text
        GameObject sObj   = new GameObject("Score");
        sObj.transform.SetParent(bg.transform, false);
        var scoreTxt = sObj.AddComponent<TMPro.TextMeshProUGUI>();
        scoreTxt.fontSize  = 28;
        scoreTxt.color     = Color.white;
        scoreTxt.fontStyle = TMPro.FontStyles.Bold;
        scoreTxt.alignment = TMPro.TextAlignmentOptions.Center;

        root.SetActive(false);

        return new TrackedObject
        {
            UI        = root,
            RT        = rootRT,
            CG        = cg,
            Borders   = new[] { top, bottom, left, right },
            LabelText = labelTxt,
            ScoreText = scoreTxt,
        };
    }

    private Image MakeLine(Transform parent, string n, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = oMin; rt.offsetMax = oMax;
        return go.AddComponent<Image>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Math helpers ────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    private static float CalcIoU(Rect a, Rect b)
    {
        float ix = Mathf.Max(a.x, b.x);
        float iy = Mathf.Max(a.y, b.y);
        float iw = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - ix);
        float ih = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - iy);
        float inter  = iw * ih;
        float unionA = a.width * a.height + b.width * b.height - inter;
        return unionA > 0f ? inter / unionA : 0f;
    }

    private static Color[] GeneratePalette(int n)
    {
        var p = new Color[n];
        for (int i = 0; i < n; i++)
            p[i] = Color.HSVToRGB((float)i / n, 0.9f, 0.9f);
        return p;
    }
}
