using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Home Screen: Premium UI with animated scanner ring, feature cards, and styled buttons.
/// All UI elements are generated purely via code — no Unity asset imports needed.
/// </summary>
public class HomeScreenUI : MonoBehaviour
{
    // Track dynamically created content so we don't duplicate on re-enable
    private GameObject _scannerRoot;
    private bool _dynamicUIBuilt  = false;
    private bool _stylingApplied  = false; // Guards against double ApplyPremiumStyling

    void Awake()
    {
        // Build dynamic elements ONCE at object creation (before AppManager disables this)
        if (!_dynamicUIBuilt)
        {
            BuildDynamicUI();
            _dynamicUIBuilt = true;
        }

        // Apply button styling ONCE here — NOT in OnEnable
        // This ensures PremiumUIAnimator is added exactly one time
        // even though AppManager will disable/re-enable this screen during startup
        if (!_stylingApplied)
        {
            ApplyPremiumStyling();
            _stylingApplied = true;
        }
    }

    void OnEnable()
    {
        // Only restart the scanner ring pulse — do NOT call ApplyPremiumStyling here
        // (that would re-trigger PremiumUIAnimator every time the screen shows)
        StopAllCoroutines();
        if (_scannerRoot != null)
            StartCoroutine(PulseScannerRing());
    }

    // ─────────────────────────────────────────────
    //  ZONE 1: Animated Scanner Ring
    // ─────────────────────────────────────────────
    private Image _outerRing;
    private Image _innerRing;
    private Image _coreDot;
    private Image _crosshairH;
    private Image _crosshairV;

    private void BuildDynamicUI()
    {
        RectTransform myRT = GetComponent<RectTransform>();
        if (myRT == null) return;

        BuildScannerRing(myRT);
    }

    private GameObject _ringsSpinner; // Only this child rotates — label stays still

    void BuildScannerRing(RectTransform parent)
    {
        // Root container — anchored BETWEEN title and subtitle
        _scannerRoot = new GameObject("ScannerRing");
        _scannerRoot.transform.SetParent(parent, false);
        RectTransform rt = _scannerRoot.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.72f);
        rt.anchorMax = new Vector2(0.5f, 0.72f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 280f);  // Was 180 — now bigger
        rt.anchoredPosition = Vector2.zero;

        // Spinner child — ONLY this rotates, so the label below stays upright
        _ringsSpinner = new GameObject("RingsSpinner");
        _ringsSpinner.transform.SetParent(_scannerRoot.transform, false);
        RectTransform spinRT = _ringsSpinner.AddComponent<RectTransform>();
        spinRT.anchorMin = new Vector2(0.5f, 0.5f);
        spinRT.anchorMax = new Vector2(0.5f, 0.5f);
        spinRT.pivot = new Vector2(0.5f, 0.5f);
        spinRT.sizeDelta = new Vector2(280f, 280f);  // Was 180
        spinRT.anchoredPosition = Vector2.zero;

        // Outer pulsing ring
        _outerRing = CreateCircleRing(_ringsSpinner.transform, "OuterRing", 280f,
            new Color(0f, 0.85f, 1f, 0.18f));

        // Middle ring
        _innerRing = CreateCircleRing(_ringsSpinner.transform, "InnerRing", 210f,
            new Color(0f, 0.85f, 1f, 0.35f));

        // Core bright dot
        _coreDot = CreateCircleRing(_ringsSpinner.transform, "CoreDot", 80f,
            new Color(0f, 0.85f, 1f, 0.9f));

        // Crosshair lines
        _crosshairH = CreateBar(_ringsSpinner.transform, "CrossH",
            new Vector2(200f, 2f), new Color(0f, 0.85f, 1f, 0.5f));
        _crosshairV = CreateBar(_ringsSpinner.transform, "CrossV",
            new Vector2(2f, 200f), new Color(0f, 0.85f, 1f, 0.5f));

        // Label — child of ROOT (not spinner), stays upright and doesn't spin
        GameObject lbl = new GameObject("ScanLabel");
        lbl.transform.SetParent(_scannerRoot.transform, false);
        RectTransform lblRT = lbl.AddComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0.5f, 0f);
        lblRT.anchorMax = new Vector2(0.5f, 0f);
        lblRT.pivot = new Vector2(0.5f, 1f);
        lblRT.anchoredPosition = new Vector2(0f, -110f); // Pushed much further down to clear rotating corners
        lblRT.sizeDelta = new Vector2(300f, 40f);
        var lblTxt = lbl.AddComponent<TextMeshProUGUI>();
        lblTxt.text = "AI DETECTION READY";
        lblTxt.fontSize = 26;
        lblTxt.color = new Color(0f, 0.85f, 1f, 0.7f);
        lblTxt.fontStyle = FontStyles.Bold;
        lblTxt.alignment = TextAlignmentOptions.Center;
    }

    Image CreateCircleRing(Transform parent, string name, float size, Color col)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.color = col;
        // Try to load built-in Unity circle sprite
        img.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        img.type = Image.Type.Simple;
        return img;
    }

    Image CreateBar(Transform parent, string name, Vector2 size, Color col)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.color = col;
        return img;
    }


    // ─────────────────────────────────────────────
    //  PULSE ANIMATION COROUTINE
    // ─────────────────────────────────────────────
    IEnumerator PulseScannerRing()
    {
        if (_outerRing == null || _innerRing == null) yield break;

        float t = 0f;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float pulse = (Mathf.Sin(t * 2.0f) + 1f) * 0.5f; // 0 → 1 → 0 loop

            // Outer ring pulses in alpha
            Color oc = _outerRing.color;
            oc.a = Mathf.Lerp(0.05f, 0.35f, pulse);
            _outerRing.color = oc;

            // Inner ring scale breathes
            if (_innerRing.rectTransform != null)
            {
                float sc = Mathf.Lerp(0.92f, 1.08f, pulse);
                _innerRing.rectTransform.localScale = new Vector3(sc, sc, 1f);
            }

            // Core dot brightness
            Color cc = _coreDot.color;
            cc.a = Mathf.Lerp(0.65f, 1f, pulse);
            _coreDot.color = cc;

            // Only the inner rings spinner rotates — label stays upright!
            if (_ringsSpinner != null)
                _ringsSpinner.transform.Rotate(0, 0, Time.unscaledDeltaTime * 18f);

            yield return null;
        }
    }

    // ─────────────────────────────────────────────
    //  PREMIUM BUTTON + TEXT STYLING
    // ─────────────────────────────────────────────
    private void ApplyPremiumStyling()
    {
        // 0. 🌌 Force Auto-Background Gradient
        Image bgImg = GetComponent<Image>();
        if (bgImg == null) bgImg = GetComponentInChildren<Image>();
        if (bgImg != null && bgImg.GetComponent<Button>() == null)
        {
            if (bgImg.GetComponent<UIGradientBackground>() == null)
            {
                var grad = bgImg.gameObject.AddComponent<UIGradientBackground>();
                grad.ApplyGradient();
            }
        }

        Color accentCyan    = new Color(0f, 0.85f, 1f, 1f);
        Color accentCyanDark= new Color(0f, 0.70f, 0.85f, 1f);
        Color softText      = new Color(0.6f, 0.65f, 0.75f, 1f);
        Color darkNavy      = new Color(0.05f, 0.08f, 0.15f, 1f);
        Color btnHov        = new Color(0.12f, 0.15f, 0.22f, 1f);

        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];

            if (btn.GetComponent<PremiumUIAnimator>() == null)
            {
                var anim = btn.gameObject.AddComponent<PremiumUIAnimator>();
                // Reduced delay so buttons appear much faster! (was 0.6f)
                anim.delay = 0.1f + i * 0.08f;  
                anim.duration = 0.35f;
            }

            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt == null) continue;

            string label = txt.text.ToLower();
            ColorBlock cb = btn.colors;
            cb.fadeDuration = 0.05f;

            if (label.Contains("general"))
            {
                cb.normalColor = accentCyan;
                cb.highlightedColor = new Color(0.2f, 0.95f, 1f, 1f);
                cb.pressedColor = accentCyanDark;
                cb.selectedColor = accentCyan;
                txt.color = darkNavy;
                txt.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            }
            else if (label.Contains("specific"))
            {
                cb.normalColor = new Color(0.08f, 0.11f, 0.18f, 0.95f);
                cb.highlightedColor = btnHov;
                cb.pressedColor = accentCyan;
                cb.selectedColor = cb.normalColor;
                txt.color = accentCyan;
                txt.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            }
            else // EXIT
            {
                Color exitBlue = new Color(0x17/255f, 0x73/255f, 0xA3/255f, 1f);
                cb.normalColor = exitBlue;
                cb.highlightedColor = new Color(0x17/255f, 0x73/255f, 0xA3/255f, 0.85f);
                cb.pressedColor = new Color(0x10/255f, 0x55/255f, 0x80/255f, 1f);
                cb.selectedColor = cb.normalColor;
                txt.color = Color.white;
                txt.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            }

            btn.colors = cb;
            Image img = btn.GetComponent<Image>();
            if (img != null && img.sprite != null)
            {
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
        }

        // Style non-button text
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            if (txt.GetComponentInParent<Button>() != null) continue;

            if (txt.text.Contains("AI-Based") || txt.fontSize > 35)
            {
                txt.color = Color.white;
                txt.fontStyle = FontStyles.Bold;
            }
            else if (txt.text.Contains("Detect up to") || txt.text.Contains("5 mechanical"))
            {
                txt.color = softText;
                txt.fontStyle = FontStyles.Normal;
            }
        }
    }

    // ─────────────────────────────────────────────
    //  BUTTON CALLBACKS
    // ─────────────────────────────────────────────
    public void OnGeneralDetection()  => AppManager.Instance.StartGeneralDetection();
    public void OnSpecificDetection() => AppManager.Instance.ShowSelection();
    public void OnExitApplication()
    {
        Debug.Log("[HomeScreenUI] Quitting application...");
        Application.Quit();
    }
}
