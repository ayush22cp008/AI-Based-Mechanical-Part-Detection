using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusUI — minimal, non-intrusive status message system.
///
/// UX Principles implemented:
///   • Messages fade in (~120ms) and fade out (~120ms) — never snap visible.
///   • Minimum display time (300ms) prevents flicker when conditions flip quickly.
///   • Cooldown (default 2s) prevents the same message from spamming the user.
///   • Canvas-based overlay — never blocks interaction (interactable=false).
///   • Creates its own UI dynamically on Awake — no Inspector wiring needed.
///   • One message at a time — new message replaces current cleanly.
///
/// The pill-shaped background auto-sizes to the text width.
/// Position: Bottom third of screen (30% from bottom) — out of main view area.
///
/// Add to any active GameObject in the Detection scene.
/// Show/hide messages from YoloSentisDetector's update loop.
/// </summary>
public class StatusUI : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Timing")]
    [Tooltip("Seconds to fade in and fade out the message.")]
    [Range(0.05f, 0.5f)]
    public float fadeDuration = 0.12f;

    [Tooltip("Minimum seconds a message stays fully visible before it can fade out.")]
    [Range(0.1f, 1.0f)]
    public float minDisplayTime = 0.30f;

    [Tooltip("Seconds before the SAME message can show again (prevents spam).")]
    [Range(0.5f, 10f)]
    public float repeatCooldown = 2.5f;

    [Header("Visual")]
    [Tooltip("Font size for the status message.")]
    public int fontSize = 36;

    [Tooltip("Position 0=bottom to 1=top along screen height.")]
    [Range(0.1f, 0.9f)]
    public float verticalPosition = 0.20f;

    // ─── Public API ───────────────────────────────────────────────────────────
    /// <summary>True while a message is visible or fading.</summary>
    public bool IsVisible => _state != State.Hidden;

    /// <summary>
    /// Request this message to appear. Respects cooldown — if the same message is
    /// on cooldown, the call is silently ignored.
    /// </summary>
    public void ShowMessage(string message)
    {
        if (message == _currentMessage && _state == State.Visible) return;  // already showing
        if (message == _lastMessage && Time.time - _lastShowTime < repeatCooldown) return;  // cooldown

        _pendingMessage = message;
        RequestShow();
    }

    /// <summary>Request the current message to begin fading out.</summary>
    public void Hide()
    {
        if (_state == State.Hidden || _state == State.FadingOut) return;

        // Respect minimum display time — delay hide if not shown long enough
        float shownFor = Time.time - _visibleSince;
        if (shownFor < minDisplayTime)
        {
            _pendingHideTime = _visibleSince + minDisplayTime;
            _pendingHide     = true;
            return;
        }

        BeginFadeOut();
    }

    // ─── Private state machine ────────────────────────────────────────────────
    private enum State { Hidden, FadingIn, Visible, FadingOut }
    private State _state = State.Hidden;

    private string _currentMessage = "";
    private string _pendingMessage  = "";
    private string _lastMessage     = "";
    private float  _lastShowTime    = float.NegativeInfinity;
    private float  _visibleSince    = 0f;
    private float  _fadeTimer       = 0f;
    private bool   _pendingHide     = false;
    private float  _pendingHideTime = 0f;

    // ─── UI objects ───────────────────────────────────────────────────────────
    private Canvas      _canvas;
    private CanvasGroup _cg;
    private RectTransform _panel;
    private TextMeshProUGUI _tmp;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        BuildUI();
    }

    void Update()
    {
        // Pending hide check (respects minDisplayTime)
        if (_pendingHide && Time.time >= _pendingHideTime)
        {
            _pendingHide = false;
            BeginFadeOut();
        }

        switch (_state)
        {
            case State.FadingIn:
                _fadeTimer         += Time.unscaledDeltaTime;
                _cg.alpha           = Mathf.Clamp01(_fadeTimer / fadeDuration);
                if (_cg.alpha >= 1f)
                {
                    _cg.alpha    = 1f;
                    _visibleSince = Time.time;
                    _state       = State.Visible;
                }
                break;

            case State.FadingOut:
                _fadeTimer         -= Time.unscaledDeltaTime;
                _cg.alpha           = Mathf.Clamp01(_fadeTimer / fadeDuration);
                if (_cg.alpha <= 0f)
                {
                    _cg.alpha = 0f;
                    _state    = State.Hidden;
                    _canvas.gameObject.SetActive(false);
                }
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────────

    void RequestShow()
    {
        if (_pendingMessage == _currentMessage && _state != State.Hidden) return;

        // Update text
        _currentMessage = _pendingMessage;
        if (_tmp != null) _tmp.text = _currentMessage;

        // Record cooldown info
        _lastMessage  = _currentMessage;
        _lastShowTime = Time.time;

        // Cancel any pending hide
        _pendingHide = false;

        if (_state == State.Hidden)
        {
            _canvas.gameObject.SetActive(true);
            _cg.alpha  = 0f;
            _fadeTimer = 0f;
            _state     = State.FadingIn;
        }
        else if (_state == State.FadingOut)
        {
            // Reverse fade direction
            _fadeTimer = fadeDuration * _cg.alpha;   // preserve current alpha
            _state     = State.FadingIn;
        }
        // If already FadingIn or Visible: text updated above, keep current fade
    }

    void BeginFadeOut()
    {
        if (_state == State.Hidden || _state == State.FadingOut) return;
        _fadeTimer = fadeDuration * _cg.alpha;   // start from current alpha
        _state     = State.FadingOut;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dynamic UI construction — creates a canvas, pill background, and TMP label
    // ─────────────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        // ── Root canvas (overlay, always on top, does not block raycasts) ──
        var canvasGO = new GameObject("StatusUI_Canvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode        = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder      = 999;   // on top of everything

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>().blockingMask = 0;  // no raycasts

        _cg                 = canvasGO.AddComponent<CanvasGroup>();
        _cg.alpha           = 0f;
        _cg.interactable    = false;
        _cg.blocksRaycasts  = false;   // never block camera touches

        // ── Pill background ────────────────────────────────────────────────
        var pillGO  = new GameObject("Pill");
        pillGO.transform.SetParent(canvasGO.transform, false);

        _panel = pillGO.AddComponent<RectTransform>();
        // Positioned at verticalPosition from bottom, centred horizontally
        _panel.anchorMin        = new Vector2(0.5f, verticalPosition);
        _panel.anchorMax        = new Vector2(0.5f, verticalPosition);
        _panel.pivot            = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta        = new Vector2(600f, 70f);  // approximate; TMP auto-sizes
        _panel.anchoredPosition = Vector2.zero;

        var pillImg  = pillGO.AddComponent<Image>();
        pillImg.color = new Color(0.05f, 0.08f, 0.15f, 0.82f);   // dark navy, semi-transparent

        // Rounded corners via PixelPerfect (simple rect if no sprite available)
        pillImg.raycastTarget = false;

        // ── Text ──────────────────────────────────────────────────────────
        var textGO = new GameObject("Label");
        textGO.transform.SetParent(pillGO.transform, false);

        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin        = Vector2.zero;
        textRT.anchorMax        = Vector2.one;
        textRT.offsetMin        = new Vector2(24f, 0f);   // horizontal padding
        textRT.offsetMax        = new Vector2(-24f, 0f);

        _tmp                    = textGO.AddComponent<TextMeshProUGUI>();
        _tmp.text               = "";
        _tmp.fontSize           = fontSize;
        _tmp.alignment          = TextAlignmentOptions.Center;
        _tmp.color              = new Color(0.9f, 0.95f, 1f);   // soft white
        _tmp.fontStyle          = FontStyles.Normal;
        _tmp.richText           = true;
        _tmp.raycastTarget      = false;
        _tmp.enableWordWrapping = false;

        // Start hidden
        canvasGO.SetActive(false);
    }
}
