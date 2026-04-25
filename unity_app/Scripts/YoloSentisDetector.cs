using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// ═══════════════════════════════════════════════════════════════════════════
/// YoloSentisDetector  —  Slim Orchestrator
/// ═══════════════════════════════════════════════════════════════════════════
///
/// Responsibilities (ONLY):
///   1. Boot AR camera and ML model.
///   2. Run motion-gated YOLO inference on a smart schedule.
///   3. Pass validated detections to ObjectTracker.
///   4. Drive ObjectTracker.UpdateUI() every frame.
///
/// Everything else lives in dedicated modules:
///   • MotionGate          — gyro/accel stability detection
///   • DetectionValidator  — rejects partial/edge/tiny boxes
///   • ObjectTracker       — lifecycle, smoothing, UI widgets
///
/// ═══════════════════════════════════════════════════════════════════════════
public class YoloSentisDetector : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("AR Foundation")]
    [Tooltip("Drag the AR Camera Manager (under XR Origin > Camera) here.")]
    public ARCameraManager    cameraManager;
    [Tooltip("Drag the AR Camera Background (under AR Camera) here.")]
    public ARCameraBackground arCameraBackground;

    [Header("YOLO Model")]
    public ModelAsset modelAsset;
    public TextAsset  classNamesFile;

    [Header("Module References")]
    [Tooltip("MotionGate component — same GameObject or assign manually.")]
    public MotionGate         motionGate;
    [Tooltip("DetectionValidator component — same GameObject or assign manually.")]
    public DetectionValidator validator;
    [Tooltip("ObjectTracker component — same GameObject or assign manually.")]
    public ObjectTracker      tracker;

    [Tooltip("SharpnessChecker component — auto-found if blank.")]
    public SharpnessChecker   sharpnessChecker;
    [Tooltip("StatusUI component — auto-found if blank.")]
    public StatusUI           statusUI;

    [Header("Inference Schedule")]
    [Range(0.05f, 2f)]
    [Tooltip("Minimum seconds between YOLO runs at peak FPS.")]
    public float baseInferenceInterval = 0.20f;

    [Range(2f, 15f)]
    [Tooltip("Force a YOLO re-run every N seconds even while tracking is stable.")]
    public float periodicRefreshInterval = 4f;

    [Header("Detection")]
    [Range(0.1f, 1f)]  public float confidenceThreshold = 0.70f;
    [Range(0.1f, 1f)]  public float iouThreshold        = 0.45f;
    [Range(1, 10)]     public int   maxDetections        = 5;

    [Header("Debug")]
    public bool debugLogs = false;

    // ─── Public API (read by DetectionScreenUI) ───────────────────────────────
    public bool   IsCameraReady            { get; private set; } = false;
    public bool   IsFilteredObjectVisible  => tracker != null && tracker.IsFilteredObjectVisible; 

    /// <summary>Pass-through so DetectionScreenUI can set the label filter.</summary>
    public string filterLabel
    {
        get => tracker != null ? tracker.filterLabel : "";
        set { if (tracker != null) tracker.filterLabel = value; }
    }

    // ─── Sentis pipeline ──────────────────────────────────────────────────────
    private const int INPUT_SIZE = 416;

    private Model            _model;
    private Worker           _worker;
    private RenderTexture    _inputRT;
    private TextureTransform _texTransform;

    private enum InferenceStage { Idle, WaitingGPU, WaitingNMS }
    private InferenceStage _stage = InferenceStage.Idle;

    private Tensor<float>                          _scheduledInput;
    private Tensor<float>                          _outputTensor;
    private Task<List<ObjectTracker.Detection>>    _nmsTask;

    private float[] _rawBuffer;        // zero-GC GPU readback buffer (allocated once)

    // ─── Timing ───────────────────────────────────────────────────────────────
    private bool  _modelLoaded        = false;
    private float _currentInterval;
    private float _lastInferenceTime  = float.NegativeInfinity;
    private float _lastRefreshTime    = float.NegativeInfinity;

    // ─── FPS monitor ─────────────────────────────────────────────────────────
    private float _fDeltaSum;
    private int   _fCount;
    private float _currentFps = 60f;

    // ─── Letterbox padding (written in BlitLetterbox, forwarded to tracker) ──
    private float _padXNorm;
    private float _padYNorm;

    // ─── Class count for background NMS closure ───────────────────────────────
    private int _numClasses = 25;

    // ─── StatusUI state ───────────────────────────────────────────────────────
    // Accumulates while camera is moving/blurry. "Hold steady" only shows
    // after kHoldSteadyDelay seconds of continuous instability to avoid spam.
    private float _notReadyDuration  = 0f;
    private const float kHoldSteadyDelay = 1.5f;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle ─────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        Application.targetFrameRate = 60;
        _currentInterval = baseInferenceInterval;
    }

    void OnEnable()
    {
        // ── Auto-wire modules if Inspector left them blank ─────────────────
        if (cameraManager      == null) cameraManager      = FindObjectOfType<ARCameraManager>();
        if (arCameraBackground == null) arCameraBackground = FindObjectOfType<ARCameraBackground>();
        if (motionGate         == null) motionGate         = GetComponent<MotionGate>()        ?? FindObjectOfType<MotionGate>();
        if (validator          == null) validator          = GetComponent<DetectionValidator>() ?? FindObjectOfType<DetectionValidator>();
        if (tracker            == null) tracker            = GetComponent<ObjectTracker>()      ?? FindObjectOfType<ObjectTracker>();
        if (sharpnessChecker   == null) sharpnessChecker   = GetComponent<SharpnessChecker>()  ?? FindObjectOfType<SharpnessChecker>();
        if (statusUI           == null) statusUI           = GetComponent<StatusUI>()           ?? FindObjectOfType<StatusUI>();

        if (tracker != null) tracker.ClearAll();
        IsCameraReady = false;
        StartCoroutine(BootSequence());
    }

    void OnDisable()
    {
        StopAllCoroutines();
        ResetPipeline();
        if (tracker != null) tracker.ClearAll();
        IsCameraReady = false;
    }

    void OnDestroy()
    {
        _worker?.Dispose();
        if (_inputRT != null) { _inputRT.Release(); _inputRT = null; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Boot ────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void InitModel()
    {
        if (_modelLoaded) return;

        // ── Load class names ──────────────────────────────────────────────
        string[] names;
        if (classNamesFile != null)
        {
            names = classNamesFile.text.Split(
                new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            names = new[]
            {
                "bearing", "bearing block", "bolt", "bushing", "chain", "clamp", "clutch",
                "collar", "coupling", "gear", "gearbox", "hydraulic cylinder", "impeller",
                "knob", "lever", "motor pump", "nut", "pulley", "screw", "seal", "shaft",
                "snap ring", "spring", "valve", "washer"
            };
        }

        _numClasses = names.Length;
        if (tracker != null) tracker.SetClassNames(names);

        // ── Load Sentis model ─────────────────────────────────────────────
        _model  = ModelLoader.Load(modelAsset);
        _worker = new Worker(_model, BackendType.GPUCompute);

        _inputRT = new RenderTexture(INPUT_SIZE, INPUT_SIZE, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Point,
            useMipMap  = false
        };
        _inputRT.Create();

        _texTransform = new TextureTransform()
            .SetDimensions(INPUT_SIZE, INPUT_SIZE, 3)
            .SetTensorLayout(TensorLayout.NCHW);

        _modelLoaded = true;
        if (debugLogs) Debug.Log($"[YOLO] Model ready — {_numClasses} classes, INPUT={INPUT_SIZE}");
    }

    IEnumerator BootSequence()
    {
        // Allow two frames for layout to finish
        yield return null;
        yield return null;

        InitModel();

        // Wait for AR subsystem (handles camera permission dialog naturally)
        while (cameraManager == null ||
               cameraManager.subsystem == null ||
               !cameraManager.subsystem.running)
            yield return null;

        // Short buffer after permission grant to flush stale frames
        yield return new WaitForSeconds(0.35f);

        _lastInferenceTime = float.NegativeInfinity;
        _lastRefreshTime   = float.NegativeInfinity;
        IsCameraReady      = true;

        if (debugLogs) Debug.Log("[YOLO] Camera READY — motion-gated detection active.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Main Update Loop ────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsCameraReady || cameraManager == null) return;

        MonitorFPS();
        TickPipeline();

        // UI update runs every frame regardless of inference state
        tracker?.UpdateUI();

        // Status message logic — smart rules, non-intrusive
        UpdateStatusUI();

        // Decide whether to kick a new inference
        TryScheduleInference();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pipeline Tick ───────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void TickPipeline()
    {
        // ── STAGE 1: Wait for GPU readback ────────────────────────────────
        if (_stage == InferenceStage.WaitingGPU &&
            _outputTensor != null &&
            _outputTensor.IsReadbackRequestDone())
        {
            Tensor<float> cpu = null;
            try   { cpu = _outputTensor.ReadbackAndClone(); }
            catch { if (debugLogs) Debug.LogWarning("[YOLO] Readback failed."); ResetPipeline(); return; }

            _scheduledInput?.Dispose();
            _scheduledInput = null;

            // Copy GPU data into our reusable float[] — zero GC allocations
            var src = cpu.AsReadOnlyNativeArray();
            if (_rawBuffer == null || _rawBuffer.Length != src.Length)
                _rawBuffer = new float[src.Length];
            src.CopyTo(_rawBuffer);

            int anchors  = cpu.shape[2];
            int nClasses = _numClasses;
            cpu.Dispose();

            // Capture locals for closure (avoid capturing 'this')
            float[] buf     = _rawBuffer;
            int     nA      = anchors;
            int     nC      = nClasses;
            float   confThr = confidenceThreshold;
            float   iouThr  = iouThreshold;
            int     maxDet  = maxDetections;

            _nmsTask = Task.Run(() => ParseAndNMS(buf, nA, nC, confThr, iouThr, maxDet));
            _stage   = InferenceStage.WaitingNMS;
        }

        // ── STAGE 2: NMS result ready ─────────────────────────────────────
        if (_stage == InferenceStage.WaitingNMS &&
            _nmsTask != null && _nmsTask.IsCompleted)
        {
            if (!_nmsTask.IsFaulted)
            {
                var rawList  = _nmsTask.Result;
                var validList = new List<ObjectTracker.Detection>(rawList.Count);

                foreach (var det in rawList)
                {
                    if (validator == null || validator.IsValid(det.Box, det.Confidence))
                        validList.Add(det);
                }

                if (tracker != null)
                {
                    tracker.SetLetterboxPadding(_padXNorm, _padYNorm);
                    tracker.FeedDetections(validList);
                }

                if (debugLogs)
                    Debug.Log($"[YOLO] Raw={rawList.Count} Valid={validList.Count}");
            }
            else if (debugLogs)
                Debug.LogWarning($"[YOLO] NMS error: {_nmsTask.Exception?.InnerException?.Message}");

            _nmsTask = null;
            _stage   = InferenceStage.Idle;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Smart Inference Scheduling ──────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void TryScheduleInference()
    {
        if (_stage != InferenceStage.Idle) return;

        // Motion gate: skip YOLO while camera is moving
        bool gateOpen = motionGate == null || motionGate.IsStable;
        if (!gateOpen) return;

        // Sharpness gate: skip YOLO if image is blurry (motion blur / out of focus).
        // Fails open (IsSharp defaults true) so missing sensor never blocks inference.
        bool imageSharp = sharpnessChecker == null || sharpnessChecker.IsSharp;
        if (!imageSharp) return;

        float now = Time.time;

        bool intervalReady  = now - _lastInferenceTime >= _currentInterval;
        bool periodicReady  = now - _lastRefreshTime   >= periodicRefreshInterval;
        bool needsReDetect  = tracker != null && tracker.NeedsReDetection();

        if (intervalReady || periodicReady || needsReDetect)
        {
            if (periodicReady) _lastRefreshTime = now;
            StartInference();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Status UI Logic ──────────────────────────────────────────────────────────────────────
    // ──────────────────────────────────────────────────────────────────────

    void UpdateStatusUI()
    {
        if (statusUI == null) return;

        bool cameraMoving = motionGate      != null && !motionGate.IsStable;
        bool imageBlurry  = sharpnessChecker != null && !sharpnessChecker.IsSharp;
        bool notReady     = cameraMoving || imageBlurry;
        bool hasObjects   = tracker != null && tracker.TrackedCount > 0;

        // Accumulate "not ready" duration to avoid spamming the message
        // on every tiny natural hand tremor
        if (notReady) _notReadyDuration += Time.deltaTime;
        else          _notReadyDuration  = 0f;

        // ══ Rule 1: "Hold steady" — only after 1.5s of continuous motion/blur
        //    AND only when no object is currently tracked.
        //    Rationale: if tracking is active the system is working — no guidance needed.
        if (notReady && _notReadyDuration >= kHoldSteadyDelay && !hasObjects)
        {
            statusUI.ShowMessage("Hold steady to scan");
            return;
        }

        // ══ Rule 2: "Detecting..." — shown briefly while inference is running
        //    Only on the very first inference after becoming stable (not every cycle).
        if (!notReady && _stage != InferenceStage.Idle && !hasObjects)
        {
            statusUI.ShowMessage("Detecting…");
            return;
        }

        // ══ Rule 3: Hide — system is idle, stable, or object already tracked
        //    Silent operation is the default. No news = good news.
        statusUI.Hide();
    }

    void StartInference()
    {
        _lastInferenceTime = Time.time;
        _stage             = InferenceStage.WaitingGPU;

        // Capture current AR frame into letterboxed RenderTexture
        BlitLetterbox();

        // Convert to Sentis tensor and schedule on GPU
        _scheduledInput = TextureConverter.ToTensor(_inputRT, _texTransform);
        _worker.Schedule(_scheduledInput);

        // Fire non-blocking readback request — GPU will DMA output async
        _outputTensor = _worker.PeekOutput("output0") as Tensor<float>;
        _outputTensor?.ReadbackRequest();

        if (debugLogs) Debug.Log("[YOLO] Inference dispatched.");
    }

    void ResetPipeline()
    {
        _scheduledInput?.Dispose();
        _scheduledInput = null;
        _nmsTask        = null;
        _stage          = InferenceStage.Idle;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Letterbox Blit ──────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void BlitLetterbox()
    {
        if (arCameraBackground?.material == null) return;

        float sW  = Screen.width;
        float sH  = Screen.height;
        float s   = Mathf.Min(INPUT_SIZE / sW, INPUT_SIZE / sH);

        _padXNorm = (INPUT_SIZE - sW * s) * 0.5f / INPUT_SIZE;
        _padYNorm = (INPUT_SIZE - sH * s) * 0.5f / INPUT_SIZE;

        var prev = RenderTexture.active;
        RenderTexture.active = _inputRT;
        GL.Clear(false, true, Color.black);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, INPUT_SIZE, INPUT_SIZE, 0);

        Rect drawRect = new Rect(
            _padXNorm * INPUT_SIZE,
            _padYNorm * INPUT_SIZE,
            sW * s,
            sH * s);

        // Draws the AR camera frame correctly oriented in a single GPU pass
        Graphics.DrawTexture(drawRect, Texture2D.whiteTexture, arCameraBackground.material);
        GL.PopMatrix();
        RenderTexture.active = prev;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NMS (Background Thread — no Unity API calls) ────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    static List<ObjectTracker.Detection> ParseAndNMS(
        float[] data, int numAnchors, int numClasses,
        float confThr, float iouThr, int maxDet)
    {
        var raw = new List<ObjectTracker.Detection>(128);

        for (int i = 0; i < numAnchors; i++)
        {
            float best = 0f;
            int   cls  = -1;

            for (int j = 0; j < numClasses; j++)
            {
                float c = data[(4 + j) * numAnchors + i];
                if (c > best) { best = c; cls = j; }
            }

            if (best < confThr || cls < 0) continue;

            float cx = data[0 * numAnchors + i];
            float cy = data[1 * numAnchors + i];
            float bw = data[2 * numAnchors + i];
            float bh = data[3 * numAnchors + i];

            raw.Add(new ObjectTracker.Detection
            {
                Box = new Rect(
                    (cx - bw * 0.5f) / INPUT_SIZE,
                    (cy - bh * 0.5f) / INPUT_SIZE,
                    bw / INPUT_SIZE,
                    bh / INPUT_SIZE),
                ClassId    = cls,
                Confidence = best
            });
        }

        // Sort by confidence — highest first
        raw.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        var  keep   = new List<ObjectTracker.Detection>(maxDet);
        var  active = new bool[raw.Count];
        for (int i = 0; i < active.Length; i++) active[i] = true;

        for (int i = 0; i < raw.Count && keep.Count < maxDet; i++)
        {
            if (!active[i]) continue;
            keep.Add(raw[i]);

            for (int j = i + 1; j < raw.Count; j++)
            {
                if (active[j] && IoU(raw[i].Box, raw[j].Box) > iouThr)
                    active[j] = false;
            }
        }

        return keep;
    }

    static float IoU(Rect a, Rect b)
    {
        float ix    = Mathf.Max(a.x, b.x);
        float iy    = Mathf.Max(a.y, b.y);
        float iw    = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - ix);
        float ih    = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - iy);
        float inter = iw * ih;
        float uni   = a.width * a.height + b.width * b.height - inter;
        return uni > 0f ? inter / uni : 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FPS Monitoring (adaptive inference throttle) ────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────

    void MonitorFPS()
    {
        _fDeltaSum += Time.unscaledDeltaTime;
        _fCount++;
        if (_fCount < 30) return;

        _currentFps = 1f / (_fDeltaSum / _fCount);
        _fDeltaSum  = 0f;
        _fCount     = 0;

        // Widen interval when FPS dips to give the GPU more breathing room
        if      (_currentFps < 35f) _currentInterval = Mathf.Min(_currentInterval + 0.05f, 0.60f);
        else if (_currentFps > 52f) _currentInterval = Mathf.Max(_currentInterval - 0.01f, baseInferenceInterval);

        if (debugLogs)
            Debug.Log($"[YOLO] FPS={_currentFps:F1}  interval={_currentInterval:F3}s  " +
                      $"gyro={motionGate?.CurrentGyroMagnitude:F3}  stable={motionGate?.IsStable}");
    }
}
