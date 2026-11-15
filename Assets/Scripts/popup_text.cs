// never used

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 
/// Animates a world-space popup (like "bounce back UP!"): bounces in, floats up, fades out, then destroys itself.
/// Fades every TextMeshPro text, UI Image and SpriteRenderer under it, so it works with any popup prefab.
/// 
public class popup_text : MonoBehaviour
{
    [SerializeField] private float lifetime = 1f;
    [SerializeField] private float rise = 0.75f;
    [Tooltip("Fraction of the lifetime spent bouncing in.")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float popInPortion = 0.25f;
    [Tooltip("Fraction of the lifetime spent fading out at the end.")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float fadePortion = 0.3f;

    private float timer;
    private Vector3 startPos;
    private Vector3 baseScale;
    private TMP_Text[] texts;
    private float[] textAlpha;
    private SpriteRenderer[] sprites;
    private Color[] spriteColors;
    private Graphic[] graphics; // UI Images etc. (TMP texts are handled above)
    private Color[] graphicColors;
    private bool started;

    /// Starts (or restarts) the popup animation with the given timing.
    public void Play(float newLifetime, float newRise)
    {
        lifetime = Mathf.Max(0.05f, newLifetime);
        rise = newRise;
        Begin();
    }

    private void Start()
    {
        if (!started) Begin(); // placed in the scene / prefab without calling Play()
    }

    private void Begin()
    {
        started = true;
        timer = 0f;
        startPos = transform.position;
        baseScale = transform.localScale;

        texts = GetComponentsInChildren<TMP_Text>();
        textAlpha = new float[texts.Length];
        for (int i = 0; i < texts.Length; i++) textAlpha[i] = texts[i].alpha;

        sprites = GetComponentsInChildren<SpriteRenderer>();
        spriteColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) spriteColors[i] = sprites[i].color;

        graphics = System.Array.FindAll(GetComponentsInChildren<Graphic>(), g => !(g is TMP_Text));
        graphicColors = new Color[graphics.Length];
        for (int i = 0; i < graphics.Length; i++) graphicColors[i] = graphics[i].color;

        transform.localScale = Vector3.zero;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float p = Mathf.Clamp01(timer / lifetime);

        // Bounce in: overshoots past full size, then settles.
        float pop = p < popInPortion ? EaseOutBack(p / popInPortion) : 1f;
        transform.localScale = baseScale * pop;

        // Float upward, slowing down.
        float r = 1f - (1f - p) * (1f - p) * (1f - p);
        transform.position = startPos + Vector3.up * rise * r;

        // Fade out at the end.
        float fadeStart = 1f - fadePortion;
        float alpha = p > fadeStart ? 1f - (p - fadeStart) / fadePortion : 1f;
        SetAlpha(alpha);

        if (timer >= lifetime) Destroy(gameObject);
    }

    private void SetAlpha(float a)
    {
        for (int i = 0; i < texts.Length; i++)
            if (texts[i] != null) texts[i].alpha = textAlpha[i] * a;

        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null) continue;
            Color c = spriteColors[i];
            c.a *= a;
            sprites[i].color = c;
        }

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null) continue;
            Color c = graphicColors[i];
            c.a *= a;
            graphics[i].color = c;
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
