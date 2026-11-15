using System;
using UnityEngine;

/// <summary>
/// Animated, pixelated retro "groovy swirl" background.
///
/// How it works: every update it draws a small low-res texture (e.g. 192 px wide) in code. Each pixel's
/// position is twisted around a few slowly drifting swirl points, run through some flowing sine waves,
/// and the result is cut into hard color bands from the palette. The texture uses point filtering and is
/// stretched to fill the camera, so you get chunky pixels. Bands slowly flow and the swirls breathe and drift.
///
/// Setup: add this to any object in the scene (an empty "Background" works). It fills the main camera's view
/// automatically and draws behind everything.
/// </summary>
public class groovy_background : MonoBehaviour
{
    [Serializable]
    public class Swirl
    {
        [Tooltip("Center, in screen-ish units (-1.8 to 1.8 across, -1 to 1 up/down).")]
        public Vector2 center;
        [Tooltip("How hard it twists. Negative = spins the other way. Too high gets noisy.")]
        public float strength = 3f;
        [Tooltip("How big the swirl is.")]
        public float radius = 0.5f;
        [Tooltip("How fast the center drifts in a small circle.")]
        public float driftSpeed = 0.05f;
        public float driftSize = 0.15f;
        [Tooltip("Offsets its drift/breathing so swirls don't move in sync.")]
        public float phase;

        public Swirl(Vector2 center, float strength, float radius, float driftSpeed, float driftSize, float phase)
        {
            this.center = center;
            this.strength = strength;
            this.radius = radius;
            this.driftSpeed = driftSpeed;
            this.driftSize = driftSize;
            this.phase = phase;
        }
    }

    [Header("Look")]
    [SerializeField] private Color[] palette =
    {
        new Color32(0x6B, 0x2C, 0x91, 0xFF), // purple
        new Color32(0xF6, 0xE6, 0xC4, 0xFF), // cream
        new Color32(0xF9, 0xB2, 0x1E, 0xFF), // yellow
        new Color32(0x1F, 0xA5, 0x9A, 0xFF), // teal
        new Color32(0xEC, 0x27, 0x6E, 0xFF), // pink
    };
    [Tooltip("The order bands repeat in, as palette indexes (0 = first palette color).")]
    [SerializeField] private int[] bandOrder = { 0, 1, 2, 3, 1, 4, 2, 1 };
    [Tooltip("Higher = thinner, more numerous bands.")]
    [SerializeField] private float bandDensity = 0.75f;
    [SerializeField] private Swirl[] swirls =
    {
        new Swirl(new Vector2(-0.85f, 0.35f), 3.2f, 0.5f, 0.05f, 0.15f, 0f),
        new Swirl(new Vector2(1.05f, -0.4f), -2.8f, 0.45f, 0.04f, 0.15f, 2f),
        new Swirl(new Vector2(0.1f, -0.05f), 1.4f, 0.8f, 0.03f, 0.1f, 4f),
    };

    [Header("Pixels")]
    [Tooltip("How many pixels across the background is. Lower = chunkier.")]
    [SerializeField] private int pixelWidth = 192;
    [Tooltip("Redraws per second. Lower = choppier, more retro (and cheaper).")]
    [SerializeField] private float updatesPerSecond = 15f;

    [Header("Motion")]
    [Tooltip("Overall animation speed.")]
    [SerializeField] private float speed = 0.6f;
    [Tooltip("How fast the color bands flow outward.")]
    [SerializeField] private float flowSpeed = 0.12f;
    [Tooltip("ON: keeps moving during the special's time stop. OFF: freezes with the game.")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Placement")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private int sortingOrder = -100;

    private SpriteRenderer sr;
    private Texture2D tex;
    private Color32[] pixels;
    private Color32[] paletteCache;
    private int texW, texH;
    private float animTime;
    private float redrawTimer;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        var obj = new GameObject("GroovyBackground");
        obj.transform.SetParent(transform, false);
        sr = obj.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;
    }

    private void LateUpdate()
    {
        if (targetCamera == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        animTime += dt * speed;

        EnsureTexture();
        FitToCamera();

        redrawTimer -= useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (redrawTimer <= 0f)
        {
            redrawTimer = 1f / Mathf.Max(1f, updatesPerSecond);
            Redraw(animTime);
        }
    }

    private void EnsureTexture()
    {
        int w = Mathf.Max(16, pixelWidth);
        int h = Mathf.Max(9, Mathf.RoundToInt(w / Mathf.Max(0.1f, targetCamera.aspect)));
        if (tex != null && w == texW && h == texH) return;

        if (tex != null) Destroy(tex);
        texW = w;
        texH = h;
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "GroovyBackground"
        };
        pixels = new Color32[w * h];
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        redrawTimer = 0f;
    }

    private void FitToCamera()
    {
        float camH = targetCamera.orthographicSize * 2f;
        float camW = camH * targetCamera.aspect;
        Vector3 c = targetCamera.transform.position;

        sr.transform.position = new Vector3(c.x, c.y, transform.position.z);
        sr.transform.localScale = new Vector3(camW / texW, camH / texH, 1f) * 1.02f; // tiny overscan, no edge gaps
        sr.sortingOrder = sortingOrder;
    }

    private void Redraw(float t)
    {
        if (paletteCache == null || paletteCache.Length != palette.Length)
            paletteCache = new Color32[palette.Length];
        for (int i = 0; i < palette.Length; i++) paletteCache[i] = palette[i];
        if (paletteCache.Length == 0 || bandOrder.Length == 0) return;

        // Swirl centers / strengths for this moment (computed once, not per pixel).
        int n = swirls.Length;
        var cx = new float[n];
        var cy = new float[n];
        var str = new float[n];
        var invR2 = new float[n];
        for (int i = 0; i < n; i++)
        {
            Swirl s = swirls[i];
            float a = t * s.driftSpeed * 2f * Mathf.PI + s.phase;
            cx[i] = s.center.x + Mathf.Cos(a) * s.driftSize;
            cy[i] = s.center.y + Mathf.Sin(a) * s.driftSize;
            str[i] = s.strength * (1f + 0.25f * Mathf.Sin(t * 0.5f + s.phase)); // gentle "breathing"
            invR2[i] = 1f / Mathf.Max(0.0001f, s.radius * s.radius);
        }

        float aspect = (float)texW / texH;
        int bands = bandOrder.Length;
        float flow = t * flowSpeed;

        for (int y = 0; y < texH; y++)
        {
            float v = ((y + 0.5f) / texH) * 2f - 1f;
            for (int x = 0; x < texW; x++)
            {
                float px = (((x + 0.5f) / texW) * 2f - 1f) * aspect;
                float py = v;

                // Twist the point around each swirl center (stronger near the center).
                for (int i = 0; i < n; i++)
                {
                    float dx = px - cx[i], dy = py - cy[i];
                    float ang = str[i] * Mathf.Exp(-(dx * dx + dy * dy) * invR2[i]);
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    px = cx[i] + dx * cs - dy * sn;
                    py = cy[i] + dx * sn + dy * cs;
                }

                // Big flowing waves, then cut into hard bands.
                float f = px * 0.9f
                        + 0.55f * Mathf.Sin(py * 2.1f + t * 0.35f)
                        + 0.35f * Mathf.Sin(px * 1.7f - py * 1.3f - t * 0.27f);
                float band = f * bandDensity - flow;
                band -= Mathf.Floor(band);

                int idx = bandOrder[Mathf.Min(bands - 1, (int)(band * bands))];
                pixels[y * texW + x] = paletteCache[Mathf.Clamp(idx, 0, paletteCache.Length - 1)];
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    private void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }
}
