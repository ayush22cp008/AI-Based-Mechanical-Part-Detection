using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central singleton — controls screen switching and detection mode.
/// Auto-finds screens by name if Inspector slots are not assigned.
/// </summary>
public class AppManager : MonoBehaviour
{
    // ── Singleton ──────────────────────────────
    public static AppManager Instance { get; private set; }

    // ── Detection state ────────────────────────
    public enum DetectionMode { General, Specific }
    public DetectionMode Mode        { get; private set; } = DetectionMode.General;
    public string        SelectedPart { get; private set; } = "";

    // ── Screen references ──────────────────────
    [Header("Screens — drag each panel here")]
    public GameObject splashScreen;
    public GameObject homeScreen;
    public GameObject selectionScreen;
    public GameObject detectionScreen;

    [Header("AR System Delayed Boot")]
    [Tooltip("Drag your 'AR Session' here and TURN IT OFF IN THE HIERARCHY so it doesn't ask for permission on startup!")]
    public GameObject arSession;
    [Tooltip("Drag your 'AR Session Origin' here and TURN IT OFF IN THE HIERARCHY as well!")]
    public GameObject arSessionOrigin;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 🔒 Lock orientation permanently — camera screen must never rotate!
        Screen.orientation = ScreenOrientation.Portrait;
        Screen.autorotateToPortrait          = true;
        Screen.autorotateToLandscapeLeft     = false;
        Screen.autorotateToLandscapeRight    = false;
        Screen.autorotateToPortraitUpsideDown = false;
        // Auto-find by name if Inspector slots are empty
        if (splashScreen    == null) splashScreen    = FindPanel("SplashScene");
        if (homeScreen      == null) homeScreen      = FindPanel("HomeScreen");
        if (selectionScreen == null) selectionScreen = FindPanel("SelectionScreen");
        if (detectionScreen == null) detectionScreen = FindPanel("DetectionScreen");

        // 🚀 Auto-fix the white gap and force responsiveness
        ForceResponsiveLayout();

        // 🎨 Auto-style the Splash Screen with the specified custom gradient
        if (splashScreen != null)
        {
            Image splashBg = splashScreen.GetComponent<Image>();
            if (splashBg == null) splashBg = splashScreen.GetComponentInChildren<Image>(); 
            
            if (splashBg != null)
            {
                UIGradientBackground grad = splashBg.GetComponent<UIGradientBackground>();
                if (grad == null) grad = splashBg.gameObject.AddComponent<UIGradientBackground>();
                
                // User's specifically requested Splash Screen Custom Gradient
                grad.topColor    = new Color(0x27 / 255f, 0x5A / 255f, 0xD3 / 255f, 1f); // #275AD3
                grad.bottomColor = new Color(0x58 / 255f, 0x18 / 255f, 0x93 / 255f, 1f); // #581893
                grad.ApplyGradient();
            }
        }

        Debug.Log($"[AppManager] Screens found: " +
                  $"Splash={splashScreen?.name ?? "MISSING"} | " +
                  $"Home={homeScreen?.name ?? "MISSING"} | " +
                  $"Selection={selectionScreen?.name ?? "MISSING"} | " +
                  $"Detection={detectionScreen?.name ?? "MISSING"}");

        // ✅ USER'S HIERARCHY FIX: Turn off all other screens instantly in Awake!
        // This solves the issue where if multiple screens (like Home and Selection) 
        // are left enabled in the Unity Hierarchy, they flash together for 1 frame at startup.
        ShowSplash();
    }

    /// <summary>
    /// Forces all screens to Full Stretch and configures the Canvas Scaler correctly.
    /// This removes white gaps and ensures the UI fills the screen on ALL devices.
    /// </summary>
    private void ForceResponsiveLayout()
    {
        // 1. Configure the Canvas Scaler for perfect mobile responsiveness
        CanvasScaler scaler = GetComponentInParent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // Standard Full HD Portrait
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f; // Balanced scaling for narrow/tall phones
        }

        // 2. Force all panels to fill the entire device screen (0px offsets)
        GameObject[] screens = { splashScreen, homeScreen, selectionScreen, detectionScreen };
        foreach (var screen in screens)
        {
            if (screen == null) continue;
            RectTransform rt = screen.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
        }
    }

    void Start()
    {
        if (splashScreen == null)
        {
            Debug.LogError("[AppManager] SplashScreen not found! " +
                           "Name your splash panel 'SplashScene' or assign it in Inspector.");
            return;
        }

        // SplashScreenUI.cs natively handles the transition to ShowHome() after its animation completes.
    }

    // ── Navigation ──────────────────────────────
    public void ShowSplash()     => ActivateOnly(splashScreen);
    public void ShowHome()       => ActivateOnly(homeScreen);
    public void ShowSelection()  => ActivateOnly(selectionScreen);
    
    private bool        _isTransitioning  = false;
    private bool        _isFirstLoad      = true;

    // Spinner overlay — used on 2nd+ visits (fast, few seconds)
    private GameObject  _spinnerOverlay;
    private CanvasGroup _spinnerCg;

    // "INITIALIZING AI CORE" text overlay — used on 1st visit only (heavier wait)
    private GameObject  _initOverlay;
    private CanvasGroup _initCg;

    // CanvasGroup on the detection screen root — keeps it invisible until camera is ready
    private CanvasGroup _detectionScreenCg;
    
    public void ShowDetection()  
    {
        if (_isTransitioning) return;
        StartCoroutine(TransitionToDetectionRoutine());
    }

    private System.Collections.IEnumerator TransitionToDetectionRoutine()
    {
        _isTransitioning = true;

        // ── Turn off all other screens ────────────────────────────────────────
        splashScreen?.SetActive(false);
        homeScreen?.SetActive(false);
        selectionScreen?.SetActive(false);

        // ── STEP 1: Cloak detection screen (alpha=0 before SetActive) ─────────
        // Unity renders one frame immediately on SetActive — BEFORE any OnEnable()
        // runs. Setting alpha=0 first makes that frame invisible → zero flash.
        if (detectionScreen != null)
        {
            _detectionScreenCg = detectionScreen.GetComponent<CanvasGroup>()
                                 ?? detectionScreen.AddComponent<CanvasGroup>();

            _detectionScreenCg.alpha          = 0f;
            _detectionScreenCg.blocksRaycasts = false;
            _detectionScreenCg.interactable   = false;

            // Tell DetectionScreenUI its own internal loading screen is not needed.
            // AppManager owns the loading experience completely.
            DetectionScreenUI.MarkAsWarmedUp();

            detectionScreen.transform.SetAsLastSibling();
            detectionScreen.SetActive(true);   // invisible — alpha=0
        }

        // ── STEP 2: Show correct loading overlay based on visit number ────────
        Transform canvasRoot = detectionScreen.transform.parent;

        if (_isFirstLoad)
        {
            // ════════════════════════════════════════════════════════════════
            // FIRST VISIT — heavy load (model + camera permission)
            // Show full-screen "INITIALIZING AI CORE" text screen.
            // ════════════════════════════════════════════════════════════════
            CreateInitOverlay(canvasRoot);
            _initOverlay.transform.SetAsLastSibling();
            _initOverlay.SetActive(true);
            _initCg.alpha = 1f;
        }
        else
        {
            // ════════════════════════════════════════════════════════════════
            // REPEAT VISIT — fast re-open (just needs camera to resume)
            // Show lightweight spinning ring overlay.
            // ════════════════════════════════════════════════════════════════
            CreateSpinnerOverlay(canvasRoot);
            _spinnerOverlay.transform.SetAsLastSibling();
            _spinnerOverlay.SetActive(true);
            _spinnerCg.alpha = 1f;
        }

        // ── STEP 3: Boot AR hardware ──────────────────────────────────────────
        // WAIT one frame *before* booting AR hardware. This lets Unity physically 
        // draw the Loading Screen on the phone display *before* AR Foundation freezes 
        // the main thread for 250ms, eliminating the "Home Screen hang" bug.
        yield return null;

        if (arSession       != null) arSession.SetActive(true);
        if (arSessionOrigin != null) arSessionOrigin.SetActive(true);

        // ── STEP 4: Wait for model + camera to be fully ready ─────────────────
        var yoloDetector = FindObjectOfType<YoloSentisDetector>();
        if (yoloDetector != null)
        {
            while (!yoloDetector.IsCameraReady)
                yield return null;
        }
        else
        {
            yield return new WaitForSeconds(0.6f);
        }

        // ── STEP 5: Buffer time to hide hardware warmup ───────────────────────
        if (_isFirstLoad)
        {
            // Gives the first YOLO inference time to warm up the GPU shader pipeline
            yield return new WaitForSeconds(2.0f);
        }
        else
        {
            // Fixes the "canvas flash" bug: Gives the camera lens time to turn on 
            // and stabilize light exposure before revealing the detection screen.
            yield return new WaitForSeconds(1.0f);
        }

        // ── STEP 6: Reveal detection screen (camera is live and stable) ───────
        if (_detectionScreenCg != null)
        {
            _detectionScreenCg.alpha          = 1f;
            _detectionScreenCg.blocksRaycasts = true;
            _detectionScreenCg.interactable   = true;
        }

        // ── STEP 7: Fade out whichever overlay was shown ──────────────────────
        CanvasGroup activeCg = _isFirstLoad ? _initCg : _spinnerCg;
        GameObject  activeGO = _isFirstLoad ? _initOverlay : _spinnerOverlay;

        if (activeCg != null)
        {
            float t = 1f;
            while (t > 0f)
            {
                t            -= Time.deltaTime * 5f;   // smooth ~0.2 s fade
                activeCg.alpha = Mathf.Max(0f, t);
                yield return null;
            }
        }
        if (activeGO != null) activeGO.SetActive(false);

        _isFirstLoad     = false;
        _isTransitioning = false;
    }

    // ─────────────────────────────────────────── overlay factories ───────────

    /// <summary>
    /// Full-screen "INITIALIZING AI CORE" text overlay — shown only on the very
    /// first visit while the ML model and camera hardware are loading.
    /// </summary>
    private void CreateInitOverlay(Transform parent)
    {
        if (_initOverlay != null) return;    // create once, reuse never (only 1st visit)

        _initOverlay = new GameObject("InitOverlay");
        _initOverlay.transform.SetParent(parent, false);

        var rt = _initOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var bg = _initOverlay.AddComponent<Image>();
        bg.color = new Color32(20, 25, 48, 255);   // same dark-navy as DetectionScreenUI

        _initCg = _initOverlay.AddComponent<CanvasGroup>();
        _initCg.blocksRaycasts = true;

        // Centred text block
        var textGO = new GameObject("InitText");
        textGO.transform.SetParent(_initOverlay.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin        = new Vector2(0.5f, 0.5f);
        textRT.anchorMax        = new Vector2(0.5f, 0.5f);
        textRT.sizeDelta        = new Vector2(850, 320);
        textRT.anchoredPosition = Vector2.zero;

        var tmp = textGO.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text      = "<color=#00FFFF><size=130%>INITIALIZING AI CORE...</size></color>\n\n" +
                        "<color=#A0AABF><size=60%>Optimizing Neural Network & Camera Hardware" +
                        "\nPlease Wait...</size></color>";
        tmp.fontSize  = 55;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.fontStyle = TMPro.FontStyles.Bold;
    }

    /// <summary>
    /// Lightweight spinning-ring overlay — shown on 2nd+ visits while the camera
    /// resumes (takes only a few seconds).
    /// </summary>
    private void CreateSpinnerOverlay(Transform parent)
    {
        if (_spinnerOverlay != null) return;   // create once, reuse on each repeat visit

        _spinnerOverlay = new GameObject("SpinnerOverlay");
        _spinnerOverlay.transform.SetParent(parent, false);

        var rt = _spinnerOverlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var bg = _spinnerOverlay.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.08f, 0.13f, 1f);

        _spinnerCg = _spinnerOverlay.AddComponent<CanvasGroup>();
        _spinnerCg.blocksRaycasts = true;

        // Spinner ring
        var spinnerGO = new GameObject("Ring");
        spinnerGO.transform.SetParent(_spinnerOverlay.transform, false);
        var spRT = spinnerGO.AddComponent<RectTransform>();
        spRT.anchoredPosition = new Vector2(0, 50);

        Color cyan = new Color(0f, 0.65f, 1f);
        for (int i = 0; i < 12; i++)
        {
            var tick   = new GameObject($"T{i}");
            tick.transform.SetParent(spinnerGO.transform, false);
            var tRT    = tick.AddComponent<RectTransform>();
            tRT.sizeDelta        = new Vector2(8, 35);
            tRT.pivot            = new Vector2(0.5f, -1.2f);
            tRT.anchoredPosition = Vector2.zero;
            tRT.localRotation    = Quaternion.Euler(0, 0, -i * 30f);
            var img              = tick.AddComponent<Image>();
            img.color            = new Color(cyan.r, cyan.g, cyan.b, 1f - (float)i / 12f);
        }
        spinnerGO.AddComponent<SpinnerAnimation>();

        // "LOADING_" label
        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(_spinnerOverlay.transform, false);
        var lblRT = lblGO.AddComponent<RectTransform>();
        lblRT.anchoredPosition = new Vector2(0, -120);
        lblRT.sizeDelta        = new Vector2(420, 100);
        var lbl                = lblGO.AddComponent<TMPro.TextMeshProUGUI>();
        lbl.text               = "LOADING_";
        lbl.fontSize           = 35;
        lbl.color              = cyan;
        lbl.alignment          = TMPro.TextAlignmentOptions.Center;
        lbl.fontStyle          = TMPro.FontStyles.Bold;
        lbl.characterSpacing   = 8f;
    }

    public void StartGeneralDetection()
    {
        Mode = DetectionMode.General;
        SelectedPart = "";
        ShowDetection(); 
    }

    public void StartSpecificDetection(string partName)
    {
        Mode = DetectionMode.Specific;
        SelectedPart = partName;
        ShowDetection();
    }

    void ActivateOnly(GameObject target)
    {
        // Disable all screens
        splashScreen?.SetActive(false);
        homeScreen?.SetActive(false);
        selectionScreen?.SetActive(false);
        detectionScreen?.SetActive(false);

        // Instantly PAUSE tracking and disable AR when leaving the detection screen
        // (Saves massive battery life and frees the camera)
        if (target != detectionScreen)
        {
            if (arSession != null) arSession.SetActive(false);
            if (arSessionOrigin != null) arSessionOrigin.SetActive(false);
        }

        // Enable only the target
        if (target != null)
        {
            target.SetActive(true);
            Debug.Log($"[AppManager] Showing: {target.name}");
        }
        else
        {
            Debug.LogWarning("[AppManager] Target screen is null — nothing to show.");
        }
    }

    /// <summary>Searches the entire scene for a GameObject with the given name.</summary>
    static GameObject FindPanel(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) Debug.LogWarning($"[AppManager] Panel '{name}' not found in scene.");
        return go;
    }
}

public class SpinnerAnimation : MonoBehaviour 
{
    void Update() 
    {
        // Spins clockwise endlessly, incredibly smoothly at screen fps
        transform.Rotate(0, 0, -360f * Time.deltaTime);
    }
}
