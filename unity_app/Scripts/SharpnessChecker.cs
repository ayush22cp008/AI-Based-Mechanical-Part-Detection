using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// SharpnessChecker — lightweight image blur detection using AR Foundation CPU images.
///
/// How it works:
///   Every <checkInterval> frames it acquires a tiny center crop (cropSize × cropSize)
///   from the AR camera's CPU-side buffer (no GPU sync), converts it to grayscale, and
///   computes the variance of the pixel values. High variance = sharp image (edges present).
///   Low variance = blurry image (motion blur or out-of-focus).
///
/// Design constraints:
///   • No GPU readback — runs entirely on CPU image data AR Foundation already provides.
///   • Only 64×64 = 4096 byte reads per check → trivially fast on any mobile CPU.
///   • Defaults IsSharp = true when no data yet, so it is fail-open (never blocks inference
///     due to sensor unavailability).
///
/// Add to the same GameObject as YoloSentisDetector. Auto-found if left blank.
/// </summary>
public class SharpnessChecker : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Sharpness Detection")]
    [Tooltip("Image variance below this threshold is considered blurry. " +
             "Tune per device — higher = stricter. Typical range: 80–400.")]
    [Range(20f, 1000f)]
    public float sharpnessThreshold = 150f;

    [Tooltip("Run sharpness check every N frames. " +
             "3 = check every 3rd frame (~20 Hz at 60 FPS). Never set to 1 (every frame).")]
    [Range(2, 15)]
    public int checkInterval = 3;

    [Tooltip("Square crop size in pixels to sample from the frame center. " +
             "64 gives good accuracy with minimal CPU cost.")]
    [Range(32, 128)]
    public int cropSize = 64;

    [Header("References (auto-found if blank)")]
    public ARCameraManager cameraManager;

    [Header("Debug")]
    public bool debugLogs = false;

    // ─── Public API ───────────────────────────────────────────────────────────
    /// <summary>True when the last measured sharpness is above threshold.
    /// Defaults to true so inference is not blocked before the first check.</summary>
    public bool  IsSharp       { get; private set; } = true;

    /// <summary>Raw pixel variance from the last successful sharpness check.</summary>
    public float SharpnessScore { get; private set; } = 9999f;

    // ─── Private ──────────────────────────────────────────────────────────────
    private int _frameSkip = 0;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (cameraManager == null)
            cameraManager = FindObjectOfType<ARCameraManager>();
    }

    void Update()
    {
        // Skip frames to reduce CPU cost
        if (++_frameSkip < checkInterval) return;
        _frameSkip = 0;
        CheckSharpness();
    }

    // ─────────────────────────────────────────────────────────────────────────
    void CheckSharpness()
    {
        if (cameraManager == null) return;

        // TryAcquireLatestCpuImage returns a CPU-side buffer — no GPU sync needed.
        if (!cameraManager.TryAcquireLatestCpuImage(out var cpuImage)) return;

        using (cpuImage)
        {
            // ── Build conversion: center crop → tiny grayscale buffer ──────
            // Alpha8 maps the luma (Y) channel to the alpha byte.
            // It is supported on both Android (camera2/ARCore) and iOS (ARKit).
            var convParams = new XRCpuImage.ConversionParams
            {
                inputRect        = CenterRect(cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(cropSize, cropSize),
                outputFormat     = TextureFormat.Alpha8,   // 1 byte per pixel (grayscale luma)
            };

            int bufSize = cpuImage.GetConvertedDataSize(convParams);
            if (bufSize <= 0) return;

            // NativeArray<byte>: allocation on temp page, freed automatically at end of block
            var buf = new NativeArray<byte>(bufSize, Allocator.Temp,
                                            NativeArrayOptions.UninitializedMemory);
            try
            {
                cpuImage.Convert(convParams, buf);
                SharpnessScore = PixelVariance(buf);
                bool wasSharp  = IsSharp;
                IsSharp        = SharpnessScore >= sharpnessThreshold;

                if (debugLogs && wasSharp != IsSharp)
                    Debug.Log($"[Sharp] Score={SharpnessScore:F1}  IsSharp={IsSharp}");
            }
            catch (System.Exception e)
            {
                // Conversion can fail if the image format changed mid-stream on some devices.
                // Fail open so inference is not permanently blocked.
                if (debugLogs) Debug.LogWarning($"[Sharp] Convert failed: {e.Message}");
                IsSharp = true;
            }
            finally
            {
                buf.Dispose();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns a square RectInt centred on the camera image.</summary>
    RectInt CenterRect(int imgW, int imgH)
    {
        int half = cropSize / 2;
        int cx   = imgW / 2;
        int cy   = imgH / 2;
        int x    = Mathf.Clamp(cx - half, 0, imgW - cropSize);
        int y    = Mathf.Clamp(cy - half, 0, imgH - cropSize);
        return new RectInt(x, y, cropSize, cropSize);
    }

    /// <summary>
    /// Computes pixel variance in one pass: Var = E[X²] − E[X]².
    /// High variance means the crop contains edges/detail (sharp).
    /// Low variance means all pixels are similar (blurry).
    /// </summary>
    static float PixelVariance(NativeArray<byte> pixels)
    {
        long sum   = 0;
        long sumSq = 0;
        int  n     = pixels.Length;

        for (int i = 0; i < n; i++)
        {
            int v  = pixels[i];
            sum   += v;
            sumSq += v * v;
        }

        float mean = (float)sum / n;
        // Var = E[X²] - E[X]²
        return ((float)sumSq / n) - (mean * mean);
    }
}
