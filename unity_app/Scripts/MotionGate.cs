using UnityEngine;

/// <summary>
/// MotionGate — monitors device motion (gyroscope + accelerometer) and controls
/// whether YOLO inference and bounding-box UI are allowed to run.
///
/// Architecture rule:
///   • IsStable == true  → camera is steady  → detection / tracking are enabled
///   • IsStable == false → camera is moving  → detection is paused, boxes hidden
///
/// This component reads motion sensors every frame and maintains a rolling
/// average so single-frame spikes don't cause false triggers.
/// </summary>
public class MotionGate : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Motion Thresholds")]
    [Tooltip("Angular velocity (rad/s) above which camera is considered 'moving'. Lower = more sensitive.")]
    [Range(0.05f, 2.0f)]
    public float gyroThreshold = 0.35f;

    [Tooltip("Linear acceleration (G) above which camera is considered 'moving'.")]
    [Range(0.02f, 1.0f)]
    public float accelThreshold = 0.18f;

    [Header("Stability Timing")]
    [Tooltip("How many consecutive stable frames before we declare the camera steady (debounce).")]
    [Range(1, 30)]
    public int stableFramesRequired = 8;

    [Tooltip("How quickly bounding boxes fade out when motion starts (alpha/s).")]
    [Range(1f, 30f)]
    public float hideSpeed = 20f;

    [Tooltip("How quickly bounding boxes fade in when motion stops (alpha/s).")]
    [Range(1f, 15f)]
    public float showSpeed = 6f;

    [Header("Debug")]
    public bool debugLogs = false;

    // ─── Public API ───────────────────────────────────────────────────────────
    /// <summary>True when device has been stable for <stableFramesRequired> frames.</summary>
    public bool IsStable { get; private set; } = true;

    /// <summary>Smooth 0–1 alpha that external UI should multiply onto CanvasGroup.alpha.</summary>
    public float UIAlpha { get; private set; } = 1f;

    /// <summary>Current smoothed gyro magnitude (rad/s) — useful for debug displays.</summary>
    public float CurrentGyroMagnitude { get; private set; }

    /// <summary>Current smoothed accelerometer magnitude (G, gravity-subtracted) — useful for debug.</summary>
    public float CurrentAccelMagnitude { get; private set; }

    // ─── Private state ────────────────────────────────────────────────────────
    private int   _stableFrameCount  = 0;
    private bool  _gyroAvailable     = false;
    private bool  _accelAvailable    = false;
    private float _smoothedGyro      = 0f;
    private float _smoothedAccel     = 0f;

    // Gravity vector tracked with a low-pass filter so we can remove it from accel
    private Vector3 _gravity = new Vector3(0f, -1f, 0f);

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _gyroAvailable  = SystemInfo.supportsGyroscope;
        _accelAvailable = SystemInfo.supportsAccelerometer;

        if (_gyroAvailable)
        {
            Input.gyro.enabled = true;
            if (debugLogs) Debug.Log("[MotionGate] Gyroscope enabled.");
        }

        if (!_gyroAvailable && !_accelAvailable)
        {
            Debug.LogWarning("[MotionGate] No motion sensors found. IsStable will always be TRUE.");
            IsStable = true;
        }
    }

    void Update()
    {
        float gyroMag  = 0f;
        float accelMag = 0f;

        // ── Gyroscope ──────────────────────────────────────────────────────
        if (_gyroAvailable)
        {
            gyroMag = Input.gyro.rotationRateUnbiased.magnitude;
        }

        // ── Accelerometer (gravity-subtracted) ────────────────────────────
        if (_accelAvailable)
        {
            // Exponential low-pass: isolate steady gravity component
            _gravity = Vector3.Lerp(_gravity, Input.acceleration, 0.1f);
            // Remaining signal = dynamic acceleration only (hand shake / movement)
            Vector3 dynamic = Input.acceleration - _gravity;
            accelMag = dynamic.magnitude;
        }

        // ── Smooth both readings (EMA) ────────────────────────────────────
        const float kSmooth = 0.25f;
        _smoothedGyro  = Mathf.Lerp(_smoothedGyro,  gyroMag,  kSmooth);
        _smoothedAccel = Mathf.Lerp(_smoothedAccel, accelMag, kSmooth);

        CurrentGyroMagnitude  = _smoothedGyro;
        CurrentAccelMagnitude = _smoothedAccel;

        // ── Motion decision ───────────────────────────────────────────────
        bool gyroMoving  = _gyroAvailable  && _smoothedGyro  > gyroThreshold;
        bool accelMoving = _accelAvailable && _smoothedAccel > accelThreshold;
        bool isMoving    = gyroMoving || accelMoving;

        if (isMoving)
        {
            _stableFrameCount = 0;
            if (IsStable && debugLogs) Debug.Log("[MotionGate] Motion detected → hiding boxes.");
            IsStable = false;
        }
        else
        {
            _stableFrameCount++;
            if (!IsStable && _stableFrameCount >= stableFramesRequired)
            {
                IsStable = true;
                if (debugLogs) Debug.Log("[MotionGate] Camera stable → enabling detection.");
            }
        }

        // ── Smooth UI Alpha ───────────────────────────────────────────────
        float targetAlpha = IsStable ? 1f : 0f;
        float speed       = IsStable ? showSpeed : hideSpeed;
        UIAlpha = Mathf.MoveTowards(UIAlpha, targetAlpha, speed * Time.deltaTime);
    }

    void OnDestroy()
    {
        if (_gyroAvailable) Input.gyro.enabled = false;
    }
}
