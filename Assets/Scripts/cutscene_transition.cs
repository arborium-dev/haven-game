using System.Collections;
using UnityEngine;
using UnityEngine.UI;


/// wave wipe for cutscenes: a single pixel-art wave (solid color with a few colored ripples on its
/// leading edge) swirls out from a point and grows over EVERYTHING (game, background and UI), then shrinks
/// back the other way. Runs on real time, so it works while time is stopped.
public class cutscene_transition : MonoBehaviour
{
    private static cutscene_transition instance;

    public static cutscene_transition Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<cutscene_transition>();
                if (instance == null) instance = new GameObject("cutscene_transition").AddComponent<cutscene_transition>();
            }
            return instance;
        }
    }

    [Header("Timing")]
    [Tooltip("Real seconds for the wave to cover the screen (and to pull back).")]
    [SerializeField] private float duration = 0.35f;

    [Header("Look")]
    [Tooltip("The wave's main color.")]
    [SerializeField] private Color waveColor = new Color32(0x6B, 0x2C, 0x91, 0xFF); // purple
    [Tooltip("Ripple stripes on the wave's leading edge, outermost first.")]
    [SerializeField] private Color[] edgeColors =
    {
        new Color32(0xEC, 0x27, 0x6E, 0xFF), // pink
        new Color32(0xF6, 0xE6, 0xC4, 0xFF), // cream
        new Color32(0xF9, 0xB2, 0x1E, 0xFF), // yellow
        new Color32(0x1F, 0xA5, 0x9A, 0xFF), // teal
        new Color32(0xF6, 0xE6, 0xC4, 0xFF), // cream
    };
    [Tooltip("Width of each edge stripe.")]
    [SerializeField] private float stripeWidth = 0.09f;
    [Tooltip("How wobbly the wave's edge is.")]
    [SerializeField] private float wobble = 0.18f;
    [Tooltip("How many wobbly lobes go around the wave.")]
    [SerializeField] private int lobes = 5;
    [Tooltip("How much the lobes swirl into a spiral.")]
    [SerializeField] private float twist = 2.2f;
    [Tooltip("Pixels across the screen. Lower = chunkier.")]
    [SerializeField] private int pixelWidth = 160;

    /// True while a scene change is in progress.
    public bool IsChangingScene { get; private set; }

    /// True while any part of the wave is on screen.
    public bool IsVisible => image != null && image.enabled;

    private Canvas canvas;
    private RawImage image;
    private Texture2D tex;
    private Color32[] pixels;
    private int texW, texH;
    private Vector2 origin = new Vector2(0.5f, 0.5f); // viewport space
    private float coverage;                           // 0 = gone, 1 = full screen

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
        // Survive scene loads so the wave can cover one scene and reveal the next.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        BuildOverlay();
    }

    /// 
    /// Grows the wave out from <paramref name="viewportOrigin"/> (0-1 screen position) until it covers everything.
    /// <paramref name="customDuration"/> overrides the normal speed (e.g. a slower victory wave).
    /// 
    public IEnumerator Cover(Vector2 viewportOrigin, float customDuration = -1f)
    {
        origin = viewportOrigin;
        yield return Animate(0f, 1f, customDuration);
    }

    /// Shrinks the wave back to where it came from, revealing the game.
    public IEnumerator Uncover(float customDuration = -1f)
    {
        yield return Animate(1f, 0f, customDuration);
    }

    /// 
    /// Wave covers the screen, the scene loads underneath it, then the wave pulls back to reveal the new scene.
    /// 
    public void TransitionToScene(string sceneName, Vector2 viewportOrigin)
    {
        if (IsChangingScene) return;
        StartCoroutine(SceneTransition(sceneName, viewportOrigin));
    }

    private IEnumerator SceneTransition(string sceneName, Vector2 viewportOrigin)
    {
        IsChangingScene = true;
        DrawOverEverything();
        if (coverage < 1f) yield return Cover(viewportOrigin);

        Time.timeScale = 1f; // never carry a time stop into the next scene
        AsyncOperation load = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
        while (load != null && !load.isDone) yield return null;
        yield return null; // let the new scene run its first frame under the wave

        yield return Uncover();
        IsChangingScene = false;
    }

    /// Normal mode: the wave draws on top of everything, UI included.
    public void DrawOverEverything()
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
    }

    /// 
    /// Draws the wave in the game world just under <paramref name="sortingOrder"/>, so sprites above that order
    /// (like the round winner) stay visible on top of it. Screen-overlay UI also stays on top.
    /// 
    public void DrawBehind(Camera cam, int sortingLayerID, int sortingOrder)
    {
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = cam != null ? Mathf.Max(cam.nearClipPlane + 0.01f, 1f) : 1f;
        canvas.sortingLayerID = sortingLayerID;
        canvas.sortingOrder = sortingOrder;
    }

    /// Instantly fully covered (e.g. right as a cutscene ends, before pulling back).
    public void ShowCovered() => SetCoverage(1f);

    /// Instantly gone (e.g. once the cutscene is showing, or on cancel).
    public void HideInstant() => SetCoverage(0f);

    /// The canvas the wave is drawn on (so other code can leave it alone when hiding UI).
    public Canvas OverlayCanvas => canvas;

    private IEnumerator Animate(float from, float to, float customDuration = -1f)
    {
        float length = customDuration > 0f ? customDuration : duration;
        float t = 0f;
        while (t < length)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, length));
            k = k * k * (3f - 2f * k); // ease in-out
            SetCoverage(Mathf.Lerp(from, to, k));
            yield return null;
        }
        SetCoverage(to);
    }

    private void SetCoverage(float c)
    {
        coverage = Mathf.Clamp01(c);
        image.enabled = coverage > 0f;
        if (image.enabled) Redraw();
    }

    // ---------- Drawing ----------

    private void BuildOverlay()
    {
        var canvasObj = new GameObject("WaveTransition");
        canvasObj.transform.SetParent(transform, false);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // above every other UI

        var imgObj = new GameObject("Wave", typeof(RectTransform));
        imgObj.transform.SetParent(canvasObj.transform, false);
        var rt = (RectTransform)imgObj.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        image = imgObj.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
    }

    private void EnsureTexture()
    {
        int w = Mathf.Max(16, pixelWidth);
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        int h = Mathf.Max(9, Mathf.RoundToInt(w / aspect));
        if (tex != null && w == texW && h == texH) return;

        if (tex != null) Destroy(tex);
        texW = w;
        texH = h;
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "WaveTransition"
        };
        pixels = new Color32[w * h];
        image.texture = tex;
    }

    private void Redraw()
    {
        EnsureTexture();

        float aspect = (float)texW / texH;
        float ox = origin.x * aspect, oy = origin.y;

        // Radius big enough that coverage 1 reaches the farthest corner, wobble and stripes included.
        float far = 0f;
        for (int i = 0; i < 4; i++)
        {
            float cx = (i & 1) * aspect, cy = (i >> 1);
            far = Mathf.Max(far, Mathf.Sqrt((cx - ox) * (cx - ox) + (cy - oy) * (cy - oy)));
        }
        int stripes = edgeColors.Length;
        float edgeWidth = stripes * stripeWidth;
        float radius = coverage * (far + wobble + edgeWidth + 0.05f);

        Color32 main = waveColor;
        Color32 clear = new Color32(0, 0, 0, 0);
        float spin = coverage * 3f; // lobes rotate as the wave grows

        for (int y = 0; y < texH; y++)
        {
            float py = (y + 0.5f) / texH - oy;
            for (int x = 0; x < texW; x++)
            {
                float px = (x + 0.5f) / texW * aspect - ox;
                float d = Mathf.Sqrt(px * px + py * py);
                float ang = Mathf.Atan2(py, px) + d * twist + spin;

                // Wobbly, swirling edge.
                float wavyD = d + wobble * Mathf.Sin(ang * lobes) * Mathf.Min(1f, d * 3f);
                float inside = radius - wavyD; // how far inside the wave's edge this pixel is

                Color32 c;
                if (inside <= 0f) c = clear;
                else if (inside >= edgeWidth) c = main;
                else c = edgeColors[Mathf.Clamp((int)(inside / stripeWidth), 0, stripes - 1)];

                pixels[y * texW + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (tex != null) Destroy(tex);
    }
}
