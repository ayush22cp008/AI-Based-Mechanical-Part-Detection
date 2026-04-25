using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PremiumUIAnimator : MonoBehaviour
{
    public float delay = 0f;
    public float duration = 0.35f;
    
    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private float timer = 0f;
    private bool isAnimating = false;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();
        
        // Hide instantly on creation to prevent frame 0 flicker before OnEnable runs
        canvasGroup.alpha = 0f;
    }

    void OnEnable()
    {
        timer = -delay;
        canvasGroup.alpha = 0f;
        
        // Scale starting slightly smaller gives a nice "pop-in" effect
        // that works beautifully inside Unity Layout Groups without fighting the positioning
        rectTransform.localScale = new Vector3(0.95f, 0.95f, 1f);
        isAnimating = true;
    }

    void Update()
    {
        if (!isAnimating) return;

        timer += Time.unscaledDeltaTime; // Use unscaled so it looks smooth even if Time.timeScale is altered
        if (timer < 0) return;

        float t = Mathf.Clamp01(timer / duration);
        
        // Very premium 'Ease-Out-Quart' curve for snappy but smooth ending
        float easeT = 1f - Mathf.Pow(1f - t, 4f);

        // Animate alpha and scale
        canvasGroup.alpha = easeT;
        rectTransform.localScale = Vector3.Lerp(new Vector3(0.90f, 0.90f, 1f), Vector3.one, easeT);

        if (t >= 1f)
        {
            isAnimating = false;
        }
    }
}
