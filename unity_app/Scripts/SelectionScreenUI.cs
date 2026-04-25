using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Selection Screen: 2-column image card grid.
/// Sprites are loaded from Assets/Resources/PartIcons/<part_name>.png at runtime.
/// Falls back gracefully to text-only if no sprite is found for a part.
/// 
/// SETUP: Place your part sprites in: Assets/Resources/PartIcons/
///        Name each sprite EXACTLY matching the part name (e.g. bolt.png, nut.png, snap ring.png)
/// </summary>
public class SelectionScreenUI : MonoBehaviour
{
    [Header("UI References")]
    public Transform       buttonContainer;
    public TextMeshProUGUI selectedLabel;
    public Button          startButton;

    [Header("Grid Layout")]
    [SerializeField] int   gridColumns   = 3;      // 3 columns to fill screen width
    [SerializeField] float cardWidth     = 300f;   // Fits 3 cols on 1080 wide canvas
    [SerializeField] float cardHeight    = 330f;   // Slightly taller than wide for portrait cards
    [SerializeField] float cardSpacingX  = 12f;   // Tight horizontal gap
    [SerializeField] float cardSpacingY  = 12f;   // Tight vertical gap
    [SerializeField] int   paddingLeft   = 12;
    [SerializeField] int   paddingRight  = 12;
    [SerializeField] int   paddingTop    = 12;
    [SerializeField] int   paddingBottom = 80;    // Extra bottom so last row clears the footer

    [Header("Card Appearance")]
    [Tooltip("Set this to match the background color inside your part sprites so whitespace becomes invisible")]
    [SerializeField] Color cardBgColor       = new Color(0.11f, 0.62f, 0.76f, 1f);  // ← Match your sprite cyan here!
    [SerializeField] Color accentColor       = new Color(0f, 0.85f, 1f, 1f);
    [SerializeField] Color selectedCardColor = new Color(0f, 0.95f, 1f, 0.35f);
    [SerializeField] Color hoverColor        = new Color(0f, 0.85f, 1f, 0.12f);
    [SerializeField] float cardAnimDelay     = 0.04f;
    [SerializeField] float cardAnimDuration  = 0.4f;

    [Header("Part Icon Settings")]
    [SerializeField] float iconRectWidth      = 260f;   // Fill most of the card width
    [SerializeField] float iconRectHeight     = 260f;   // Fill most of the card height
    [SerializeField] float iconLayoutWidth    = 260f;
    [SerializeField] float iconLayoutHeight   = 260f;
    [SerializeField] bool  iconPreserveAspect = false;  // FALSE = uniform fill regardless of sprite whitespace
    [SerializeField] Color iconTint           = Color.white;
    [SerializeField] Color iconPlaceholder    = new Color(0f, 0.85f, 1f, 0.15f);

    [Header("Part Label Settings")]
    [Tooltip("Disable to hide text — images already have names printed on them")]
    [SerializeField] bool  showPartLabel     = false;
    [SerializeField] int   labelFontSize     = 22;
    [SerializeField] Color labelColor        = new Color(0.70f, 0.75f, 0.85f, 1f);
    [SerializeField] float labelLayoutHeight = 50f;

    [Header("Card Inner Padding")]
    [SerializeField] int cardVlgLeft    = 0;   // Zero padding — maximize image area inside card
    [SerializeField] int cardVlgRight   = 0;
    [SerializeField] int cardVlgTop     = 0;
    [SerializeField] int cardVlgBottom  = 0;
    [SerializeField] int cardVlgSpacing = 0;

    // Parts ordered from most-used → least-used
    static readonly string[] PartOrder = new string[]
    {
        "bolt", "nut", "screw", "washer", "bearing", "shaft", "gear", "spring",
        "seal", "snap ring", "pulley", "coupling", "clamp", "lever", "collar",
        "bushing", "bearing block", "chain", "knob", "valve", "impeller",
        "motor pump", "gearbox", "hydraulic cylinder", "clutch",
    };

    string _selectedPart = "";

    // Track card GameObjects to update selection highlight
    readonly List<(GameObject card, string partName)> _cards = new List<(GameObject, string)>();

    void OnEnable()
    {
        StopAllCoroutines();
        ApplyViewportAndFooterStyling();
        StartCoroutine(BuildImageGridRoutine());
        _selectedPart = "";
        if (selectedLabel != null) selectedLabel.text = "";
        if (startButton   != null) startButton.interactable = false;
    }

    // ─────────────────────────────────────────────────────────────────
    //  GRID BUILDER
    // ─────────────────────────────────────────────────────────────────
    IEnumerator BuildImageGridRoutine()
    {
        if (buttonContainer == null) { Debug.LogError("[SelectionScreen] buttonContainer is NULL!"); yield break; }

        Debug.Log("[SelectionScreen] Starting grid build...");

        // Clear old cards
        _cards.Clear();
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        yield return null;

        try
        {
            SetupGridLayout();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SelectionScreen] SetupGridLayout FAILED: {e.Message}\n{e.StackTrace}");
            yield break;
        }

        Debug.Log($"[SelectionScreen] Grid setup done. Building {PartOrder.Length} cards...");

        for (int i = 0; i < PartOrder.Length; i++)
        {
            string partName = PartOrder[i];
            Sprite icon = Resources.Load<Sprite>($"PartIcons/{partName}");
            if (icon == null) Debug.LogWarning($"[SelectionScreen] No sprite found for: PartIcons/{partName}");

            try
            {
                GameObject card = BuildPartCard(partName, icon, i);
                _cards.Add((card, partName));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SelectionScreen] BuildPartCard FAILED for '{partName}': {e.Message}");
            }

            if (i % 4 == 0) yield return null;
        }

        Debug.Log($"[SelectionScreen] Done! {_cards.Count} cards built.");
        HighlightSelected("");
    }

    void SetupGridLayout()
    {
        var oldVlg = buttonContainer.GetComponent<VerticalLayoutGroup>();
        if (oldVlg != null) DestroyImmediate(oldVlg);
        var oldCsf = buttonContainer.GetComponent<ContentSizeFitter>();
        if (oldCsf != null) DestroyImmediate(oldCsf);
        var oldGrid = buttonContainer.GetComponent<GridLayoutGroup>();
        if (oldGrid != null) DestroyImmediate(oldGrid);

        GridLayoutGroup grid = buttonContainer.gameObject.AddComponent<GridLayoutGroup>();
        if (grid == null) { Debug.LogError("[SelectionScreen] Failed to add GridLayoutGroup!"); return; }

        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = gridColumns;
        grid.cellSize        = new Vector2(cardWidth, cardHeight);
        grid.spacing         = new Vector2(cardSpacingX, cardSpacingY);
        grid.padding         = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
        grid.childAlignment  = TextAnchor.UpperCenter;

        ContentSizeFitter csf = buttonContainer.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    GameObject BuildPartCard(string partName, Sprite icon, int index)
    {
        // ── Card Root (Button) ──────────────────────────────────
        GameObject card = new GameObject($"Card_{partName}");
        card.transform.SetParent(buttonContainer, false);
        card.AddComponent<RectTransform>();

        Image cardBg = card.AddComponent<Image>();
        cardBg.color = cardBgColor; // Matches sprite background so whitespace is invisible

        Button btn = card.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor      = cardBgColor;   // Same as bg so no color flash on enable
        cb.highlightedColor = hoverColor;
        cb.pressedColor     = accentColor;
        cb.selectedColor    = cardBgColor;
        cb.fadeDuration     = 0.05f;
        btn.colors = cb;

        string captured = partName;
        btn.onClick.AddListener(() => SelectPart(captured));

        // PremiumUIAnimator is optional — skip if script doesn't exist in project
        var animType = System.Type.GetType("PremiumUIAnimator");
        if (animType != null)
        {
            var anim = card.AddComponent(animType) as MonoBehaviour;
            if (anim != null)
            {
                animType.GetField("delay")?.SetValue(anim, index * cardAnimDelay);
                animType.GetField("duration")?.SetValue(anim, cardAnimDuration);
            }
        }

        // ── Vertical layout inside card ─────────────────────────
        VerticalLayoutGroup vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment         = TextAnchor.MiddleCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = true;
        vlg.padding = new RectOffset(cardVlgLeft, cardVlgRight, cardVlgTop, cardVlgBottom);
        vlg.spacing = cardVlgSpacing;

        // ── Part Icon Image ─────────────────────────────────────
        GameObject iconObj = new GameObject("PartIcon");
        iconObj.transform.SetParent(card.transform, false);
        RectTransform iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.sizeDelta = new Vector2(iconRectWidth, iconRectHeight);  // Inspector: Part Icon Settings

        Image iconImg = iconObj.AddComponent<Image>();
        if (icon != null)
        {
            iconImg.sprite = icon;
            iconImg.preserveAspect = iconPreserveAspect;
            iconImg.color = iconTint;
        }
        else
        {
            iconImg.color = iconPlaceholder; // Inspector: tint shown when no sprite found
        }

        LayoutElement iconLE = iconObj.AddComponent<LayoutElement>();
        iconLE.preferredWidth  = iconLayoutWidth;
        iconLE.preferredHeight = iconLayoutHeight;
        iconLE.flexibleWidth   = 1f;
        iconLE.flexibleHeight  = 0f;

        // ── Part Name Label (hidden by default — images already have names on them) ──
        GameObject lblObj = new GameObject("PartLabel");
        lblObj.transform.SetParent(card.transform, false);
        lblObj.AddComponent<RectTransform>();
        lblObj.SetActive(showPartLabel); // Toggle from inspector

        TextMeshProUGUI lbl = lblObj.AddComponent<TextMeshProUGUI>();
        lbl.text      = Capitalize(partName);
        lbl.fontSize  = labelFontSize;
        lbl.color     = labelColor;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.enableWordWrapping = true;

        LayoutElement lblLE = lblObj.AddComponent<LayoutElement>();
        lblLE.preferredHeight = labelLayoutHeight;  // Inspector: Part Label Settings
        lblLE.flexibleWidth   = 1f;

        return card;
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECTION HIGHLIGHT
    // ─────────────────────────────────────────────────────────────────
    void SelectPart(string part)
    {
        _selectedPart = part;
        if (startButton != null) startButton.interactable = true;
        if (selectedLabel != null) selectedLabel.text = Capitalize(part);
        HighlightSelected(part);
    }

    void HighlightSelected(string selected)
    {
        Color accentCyan = new Color(0f, 0.85f, 1f, 1f);
        Color darkCard   = new Color(0.06f, 0.09f, 0.16f, 0.92f);
        Color textNormal = new Color(0.70f, 0.75f, 0.85f, 1f);
        Color darkNavy   = new Color(0.02f, 0.04f, 0.08f, 1f);

        foreach (var (card, partName) in _cards)
        {
            if (card == null) continue;
            bool isSelected = partName.Equals(selected, System.StringComparison.OrdinalIgnoreCase);

            // Card background — transparent when unselected, subtle cyan when selected
            Image bg = card.GetComponent<Image>();
            if (bg != null)
                bg.color = isSelected ? selectedCardColor : cardBgColor;

            Button btn = card.GetComponent<Button>();
            if (btn != null)
            {
                ColorBlock cb = btn.colors;
                cb.normalColor = isSelected ? selectedCardColor : cardBgColor;
                btn.colors = cb;
            }

            // Label color
            TextMeshProUGUI lbl = card.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
            {
                lbl.color = isSelected ? accentCyan : textNormal;
                lbl.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  VIEWPORT AND FOOTER STYLING
    // ─────────────────────────────────────────────────────────────────
    private void ApplyViewportAndFooterStyling()
    {
        // Background gradient
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

        // Scroll settings
        if (buttonContainer != null)
        {
            ScrollRect scrollRect = buttonContainer.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                if (scrollRect.verticalScrollbar != null)
                    scrollRect.verticalScrollbar.gameObject.SetActive(false);

                Image scrollBg = scrollRect.GetComponent<Image>();
                if (scrollBg != null)
                    scrollBg.color = new Color(0.02f, 0.04f, 0.08f, 0.4f);
            }
        }

        // Footer buttons
        Color accentCyan    = new Color(0f, 0.85f, 1f, 1f);
        Color accentCyanDark= new Color(0f, 0.70f, 0.85f, 1f);
        Color darkNavy      = new Color(0.05f, 0.08f, 0.15f, 1f);

        Button[] allButtons = GetComponentsInChildren<Button>(true);
        foreach (var btn in allButtons)
        {
            if (buttonContainer != null && btn.transform.IsChildOf(buttonContainer)) continue;
            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt == null) continue;

            string label = txt.text.ToLower();
            ColorBlock cb = btn.colors;
            cb.fadeDuration = 0.05f;

            if (label.Contains("start") || label.Contains("detection"))
            {
                cb.normalColor = accentCyan; cb.highlightedColor = new Color(0.2f, 0.95f, 1f, 1f);
                cb.pressedColor = accentCyanDark; cb.selectedColor = accentCyan;
                txt.color = darkNavy;
                txt.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            }
            else
            {
                Color customBack = new Color(0x17/255f, 0x73/255f, 0xA3/255f, 1f);
                cb.normalColor = customBack; cb.highlightedColor = new Color(0x17/255f, 0x73/255f, 0xA3/255f, 0.85f);
                cb.pressedColor = new Color(0x10/255f, 0x55/255f, 0x80/255f, 1f); cb.selectedColor = customBack;
                txt.color = Color.white;
                txt.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            }
            btn.colors = cb;
            Image img = btn.GetComponent<Image>();
            if (img != null && img.sprite != null) { img.type = Image.Type.Sliced; img.color = Color.white; }
        }
    }

    public void OnStartDetection()
    {
        if (string.IsNullOrEmpty(_selectedPart)) return;
        AppManager.Instance.StartSpecificDetection(_selectedPart);
    }

    public void OnBack() => AppManager.Instance.ShowHome();

    static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s.Substring(1);
    }
}
