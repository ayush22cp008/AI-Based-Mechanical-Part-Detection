using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DetectionScreenUI : MonoBehaviour
{
    [Header("Main HUD References")]
    public TextMeshProUGUI modeLabel;
    public Button         backButton; 

    // --- Add a reference to the AI script ---
    private YoloSentisDetector _yoloDetector;

    void Awake()
    {
        // Find the detector on this same screen
        _yoloDetector = GetComponent<YoloSentisDetector>();
        CreateLoadingScreen();
    }

    void OnEnable()
    {
        // 1. Force Screen Stretch
        RectTransform panelRT = GetComponent<RectTransform>();
        if (panelRT != null) {
            panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero; panelRT.offsetMax = Vector2.zero;
        }

        // 2. Hide any unreferenced white squares
        foreach (Transform child in transform) {
            Image img = child.GetComponent<Image>();
            if (img != null && img.color == Color.white && img.gameObject != backButton?.gameObject) {
                img.gameObject.SetActive(false);
            }
        }

        // 3. Style HUD Labels & SET AI FILTER
        if (modeLabel != null && AppManager.Instance != null && _yoloDetector != null)
        {
            if (AppManager.Instance.Mode == AppManager.DetectionMode.General)
            {
                modeLabel.text = "<color=#00FFFF><size=140%>GENERAL SCAN</size></color>\n<color=#FFFFFF><size=80%>STATUS: ACTIVE</size></color>";
                _yoloDetector.filterLabel = ""; // No filter for general scan
            }
            else
            {
                string target = AppManager.Instance.SelectedPart;
                // Default label — no FOUND suffix until a box appears
                modeLabel.text = $"<color=#00FFFF><size=140%>TARGET SCAN</size></color>\n<color=#FFFFFF><size=80%>SEARCHING FOR: <color=#00FFFF>{target.ToUpper()}</color></size></color>";
                _yoloDetector.filterLabel = target; // Tell the AI to only show this part
            }

            modeLabel.alignment = TextAlignmentOptions.Center;
            RectTransform rt = modeLabel.GetComponent<RectTransform>();
            // Use % anchors so label sits ~8% from top on ANY screen size (no fixed pixel hacks)
            rt.anchorMin = new Vector2(0f, 0.88f);
            rt.anchorMax = new Vector2(1f, 0.96f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(20f, 0f);   // small side padding
            rt.offsetMax = new Vector2(-20f, 0f);
            rt.anchoredPosition = Vector2.zero;
        }

        // 4. Back Button Listener
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBack);
        }

        // Reset found-state so the first Update() frame always writes the correct label
        _wasFound = false;

        // 5. Trigger Initial Clean Loading Screen
        if (!_hasGlobalWarmedUp) {
            _loadingScreen.SetActive(true);
            _isLoading = true;
            _timeSinceEnable = 0f;
            _framesSinceEnable = 0;
            
            // Hide the HUD text so it doesn't clip through the loading screen
            if (modeLabel != null) modeLabel.gameObject.SetActive(false);
        } else {
            _loadingScreen.SetActive(false);
            _isLoading = false;
            
            // Restore everything instantly if already warmed up
            if (modeLabel != null) modeLabel.gameObject.SetActive(true);
        }
    }

    // --- Loading UI Initialization ---
    private GameObject _loadingScreen;
    private static bool _hasGlobalWarmedUp = false;
    private bool _isLoading = false;
    private float _timeSinceEnable = 0f;
    private int _framesSinceEnable = 0;

    /// <summary>
    /// Called by AppManager BEFORE SetActive(true) so DetectionScreenUI knows
    /// that the loading experience is already being handled externally.
    /// This prevents the internal 'INITIALIZING AI CORE' screen from ever showing.
    /// </summary>
    public static void MarkAsWarmedUp() => _hasGlobalWarmedUp = true;

    private void CreateLoadingScreen() {
        if (_loadingScreen != null) return;

        // Dynamically build a full-screen masking panel covering the lag
        _loadingScreen = new GameObject("AI_LoadingScreen");
        _loadingScreen.transform.SetParent(this.transform, false);
        _loadingScreen.transform.SetAsLastSibling(); // Force to front of UI!
        
        RectTransform rt = _loadingScreen.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        
        Image bg = _loadingScreen.AddComponent<Image>();
        bg.color = new Color32(32, 37, 64, 255); // Rich Dark Navy / Indigo matching the Specific Detection UI
        
        GameObject textObj = new GameObject("LoadingText");
        textObj.transform.SetParent(_loadingScreen.transform, false);
        RectTransform txtRt = textObj.AddComponent<RectTransform>();
        txtRt.anchorMin = new Vector2(0.5f, 0.5f); txtRt.anchorMax = new Vector2(0.5f, 0.5f);
        txtRt.sizeDelta = new Vector2(800, 300);
        txtRt.anchoredPosition = Vector2.zero;
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "<color=#00FFFF><size=130%>INITIALIZING AI CORE...</size></color>\n\n<color=#A0AABF><size=60%>Optimizing Neural Network & Camera Hardware\nPlease Wait...</size></color>";
        tmp.fontSize = 55;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        
        _loadingScreen.SetActive(false);
    }

    // --- Runtime detection status tracking ---
    // Cached so we only rebuild the TMP string on actual state transitions, not every frame
    private bool _wasFound = false;

    void Update()
    {
        // Handle Loading Screen un-masking
        if (_isLoading) {
            // 🚀 CRITICAL FIX: The YOLO Detector spawns UI boxes rapidly at runtime.
            // By constantly forcing the loading screen to the very end of the hierarchy list,
            // Unity naturally draws the solid Indigo background completely OVER the boxes!
            if (_loadingScreen != null) {
                _loadingScreen.transform.SetAsLastSibling();
            }

            // PERFECT SYNC: Listen directly to the YOLO Detector instead of a broken hardcoded 2.0s timer!
            // When IsCameraReady is 1, the camera and AI are officially running perfectly.
            if (_yoloDetector != null && _yoloDetector.IsCameraReady) {
                _isLoading = false;
                _hasGlobalWarmedUp = true;
                
                // Instantly remove the loading screen to prevent any detection bleeding!
                _loadingScreen.SetActive(false);
                
                // Restore HUD visibility!
                if (modeLabel != null) modeLabel.gameObject.SetActive(true);
            }
            return; // Block UI label updates while loading
        }

        // Only poll in Specific mode — General mode label is static
        if (modeLabel == null || _yoloDetector == null || AppManager.Instance == null) return;
        if (AppManager.Instance.Mode != AppManager.DetectionMode.Specific) return;

        // IsFilteredObjectVisible is set by the detector at the end of UpdateSmoothBoxes(),
        // so it is always in sync with the current frame's bounding-box visibility.
        bool isFound = _yoloDetector.IsFilteredObjectVisible;

        // Only reconstruct the string when the boolean actually flips
        // (avoids per-frame GC allocation while remaining frame-perfectly synced)
        if (isFound == _wasFound) return;
        _wasFound = isFound;

        string upper = AppManager.Instance.SelectedPart.ToUpper();

        if (isFound)
        {
            // ✅ Object bounding box is visible → append FOUND in highlight green
            modeLabel.text =
                "<color=#00FFFF><size=140%>TARGET SCAN</size></color>\n" +
                $"<color=#FFFFFF><size=80%>SEARCHING FOR: <color=#00FFFF>{upper}</color>" +
                " <color=#00FF88>FOUND</color></size></color>";
        }
        else
        {
            // ❌ No bounding box visible → revert to plain searching text
            modeLabel.text =
                "<color=#00FFFF><size=140%>TARGET SCAN</size></color>\n" +
                $"<color=#FFFFFF><size=80%>SEARCHING FOR: <color=#00FFFF>{upper}</color></size></color>";
        }
    }

    public void OnBack()
    {
        // Clear filter before leaving
        if (_yoloDetector != null) _yoloDetector.filterLabel = "";
        AppManager.Instance.ShowHome();
    }
}
