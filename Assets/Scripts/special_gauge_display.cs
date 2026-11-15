using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Shows a player's special gauge as a pixel-art circle outline around them that fills up clockwise:
/// blue for gauge earned with normal hits, yellow for barely hits.
/// When it fills, the ring bursts, turns purple and keeps pulsing until the special is used,
/// and the "bounce back UP!" text box pops up.
///
/// Add this to the same object as player_movemen. Bots don't have a gauge, so it hides itself for them.
/// </summary>
[RequireComponent(typeof(player_movemen))]
public class special_gauge_display : MonoBehaviour
{
    [Header("Ring")]
    [Tooltip("Draw the ring around the paddle, right on the line between normal and barely hits " +
             "(ball inside the ring = normal hit, outside = barely). " +
             "Turn off to use Radius / Offset instead.")]
    [SerializeField] private bool matchBarelyLine = true;
    [Tooltip("Ring radius in world units when not matching the barely line. 0 = fit around the player's sprite.")]
    [SerializeField] private float radius = 0f;
    [SerializeField] private Vector2 offset = Vector2.zero;
    [Tooltip("Ring size in pixels (higher = smoother circle, smaller pixels).")]
    [SerializeField] private int resolution = 32;
    [Tooltip("Ring line thickness in pixels.")]
    [SerializeField] private int thickness = 2;
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.25f);
    [Tooltip("Ring color for gauge earned from normal hits.")]
    [SerializeField] private Color normalHitColor = new Color(0.25f, 0.55f, 1f, 1f);
    [Tooltip("Ring color for gauge earned from barely hits.")]
    [FormerlySerializedAs("fillColor")]
    [SerializeField] private Color barelyHitColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Whole ring turns this color once the special is ready.")]
    [SerializeField] private Color readyColor = new Color(1f, 0.35f, 0.85f, 1f);
    [Tooltip("How fast the ring visually catches up to the gauge (gauge fraction per second).")]
    [SerializeField] private float fillSpeed = 1.5f;
    [Tooltip("Draw order relative to the player's sprite (+1 = in front).")]
    [SerializeField] private int sortingOrderOffset = 1;
    [Tooltip("Real seconds for a full ring to drain away when the gauge is emptied (special used / point reset). " +
             "Runs even while time is stopped.")]
    [SerializeField] private float drainDuration = 0.6f;

    [Header("Full Gauge Animation")]
    [SerializeField] private float burstScale = 1.5f;
    [SerializeField] private float burstDuration = 0.35f;
    [Tooltip("Gentle pulse while the special is ready.")]
    [SerializeField] private float pulseSpeed = 6f;
    [SerializeField] private float pulseAmount = 0.08f;
    //
    // [Header("Special Ready Popup")]
    // [Tooltip("Optional. Your own popup prefab. Can be UI (TextMeshPro - Text (UI), Images) or world-space " +
    //          "(TextMeshPro - Text, SpriteRenderers). If empty, a text box is built automatically.")]
    // [SerializeField] private GameObject popupPrefab;
    // [Tooltip("For UI popup prefabs: world units per UI pixel (0.02 = a 200px wide prefab is 4 units wide).")]
    // [SerializeField] private float popupCanvasScale = 0.02f;
    // [SerializeField] private string popupMessage = "bounce back UP!";
    // [Tooltip("TextMeshPro font for the auto-built popup. Empty = TextMeshPro's default font.")]
    // [SerializeField] private TMP_FontAsset popupFont;
    // [SerializeField] private float popupFontSize = 4f;
    // [SerializeField] private Color popupTextColor = Color.white;
    // [SerializeField] private Color popupBoxColor = new Color(0.12f, 0.1f, 0.25f, 0.9f);
    // [SerializeField] private Color popupBorderColor = new Color(1f, 0.85f, 0.2f, 1f);
    // [Tooltip("Size of one 'pixel' of the text box border, in world units.")]
    // [SerializeField] private float popupBoxPixelSize = 0.07f;
    // [SerializeField] private Vector2 popupPadding = new Vector2(0.25f, 0.12f);
    // [SerializeField] private Vector2 popupOffset = new Vector2(0f, 1.5f);
    // [SerializeField] private float popupLifetime = 1f;
    // [SerializeField] private float popupRise = 0.75f;
    // [SerializeField] private int popupSortingOrder = 100;

    private player_movemen player;
    private SpriteRenderer ringRenderer;
    private Texture2D ringTexture;
    private Color32[] pixels;

    // Pieces of the gauge in the order they were earned: where each ends (0-1) and if it came from a barely hit.
    private readonly List<(float end, bool barely)> segments = new List<(float end, bool barely)>();
    private int segmentsVersion, drawnSegmentsVersion = -1;

    // While draining: the old colors for the part of the ring that's emptying out.
    private readonly List<(float end, bool barely)> drainSegments = new List<(float end, bool barely)>();
    private bool draining;
    private bool drainWasReady;

    private float displayedFill;
    private float drawnFill = -1f;
    private bool drawnReady;
    private bool wasReady;
    private float burstTimer;

    private static Sprite boxSprite;
    private static Color32 boxSpriteFill, boxSpriteBorder;

    private void Awake()
    {
        player = GetComponent<player_movemen>();
        BuildRing();
    }

    private void OnEnable()
    {
        if (player == null) player = GetComponent<player_movemen>();
        player.OnSpecialGained += HandleSpecialGained;
        // player.OnSpecialReady += HandleSpecialReady;
    }

    private void OnDisable()
    {
        player.OnSpecialGained -= HandleSpecialGained;
        // player.OnSpecialReady -= HandleSpecialReady;
        if (ringRenderer != null) ringRenderer.enabled = false;
    }

    private void HandleSpecialGained(float from, float to, bool barely)
    {
        segments.Add((to, barely));
        segmentsVersion++;
    }

    private void LateUpdate()
    {
        bool show = player.HasSpecialGauge;
        ringRenderer.enabled = show;
        if (!show) return;

        // Follow the paddle (it moves with the player and flips sides).
        if (matchBarelyLine)
        {
            Vector2 p = player.PaddlePoint;
            ringRenderer.transform.position = new Vector3(p.x, p.y, transform.position.z);
        }

        // Ring catches up smoothly when filling, and drains away when the gauge is emptied.
        float target = player.SpecialNormalized;
        if (target < displayedFill)
        {
            if (!draining)
            {
                // Gauge was emptied: the old colors drain out, new gauge starts with fresh colors.
                draining = true;
                drainWasReady = drawnReady;
                drainSegments.Clear();
                drainSegments.AddRange(segments);
                segments.Clear();
                segmentsVersion++;
            }
            // Unscaled, so it drains during the special's time stop too.
            displayedFill = Mathf.MoveTowards(displayedFill, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, drainDuration));
        }
        else
        {
            draining = false;
            displayedFill = Mathf.MoveTowards(displayedFill, target, fillSpeed * Time.deltaTime);
        }

        bool ready = player.SpecialIsFull;
        if (ready && !wasReady) burstTimer = burstDuration; // just filled up: play the burst
        wasReady = ready;

        RedrawIfNeeded(displayedFill, ready && displayedFill >= 1f);
        AnimateRing(ready);
    }

    // ---------- Ring ----------

    private void BuildRing()
    {
        resolution = Mathf.Max(8, resolution);

        float worldRadius = radius;
        if (matchBarelyLine)
        {
            // Size it so the middle of the line sits exactly on the barely-hit boundary.
            float halfRes = resolution / 2f;
            float lineMiddle = halfRes - Mathf.Max(1, thickness) / 2f;
            worldRadius = player.BarelyRadius * halfRes / lineMiddle;
        }
        else if (worldRadius <= 0f && TryGetComponent(out SpriteRenderer playerSprite))
        {
            worldRadius = Mathf.Max(playerSprite.bounds.extents.x, playerSprite.bounds.extents.y) * 1.1f;
        }
        if (worldRadius <= 0f) worldRadius = 1f;

        ringTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "SpecialRing"
        };
        pixels = new Color32[resolution * resolution];

        // Pixels-per-unit chosen so the ring is the right world size even if the player is scaled.
        float parentScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        float ppu = resolution / (worldRadius * 2f / parentScale);

        var ringObj = new GameObject("SpecialRing");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = offset / parentScale;

        ringRenderer = ringObj.AddComponent<SpriteRenderer>();
        ringRenderer.sprite = Sprite.Create(ringTexture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), ppu);
        if (TryGetComponent(out SpriteRenderer sr))
        {
            ringRenderer.sortingLayerID = sr.sortingLayerID;
            ringRenderer.sortingOrder = sr.sortingOrder + sortingOrderOffset;
        }

        RedrawIfNeeded(0f, false);
    }

    private void RedrawIfNeeded(float fill, bool ready)
    {
        // Only rebuild the texture when the change is big enough to move a pixel.
        if (Mathf.Abs(fill - drawnFill) < 0.005f && ready == drawnReady && segmentsVersion == drawnSegmentsVersion) return;
        drawnFill = fill;
        drawnReady = ready;
        drawnSegmentsVersion = segmentsVersion;

        Color32 off = emptyColor;
        Color32 clear = new Color32(0, 0, 0, 0);

        float center = resolution / 2f;
        float outer = center;
        float inner = outer - Mathf.Max(1, thickness);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                Color32 c = clear;
                if (d <= outer && d > inner)
                {
                    // 0 at 12 o'clock, going clockwise to 1.
                    float a = Mathf.Atan2(dx, dy) / (2f * Mathf.PI);
                    if (a < 0f) a += 1f;
                    c = a <= fill ? FilledColorAt(a, ready) : off;
                }
                pixels[y * resolution + x] = c;
            }
        }

        ringTexture.SetPixels32(pixels);
        ringTexture.Apply();
    }

    /// <summary>
    /// Color of the filled ring at point a (0-1): purple when ready, otherwise blue/yellow by how it was earned.
    /// The part that's draining away keeps the colors it had (purple if the special was ready).
    /// </summary>
    private Color32 FilledColorAt(float a, bool ready)
    {
        if (ready) return readyColor;
        if (draining && a > player.SpecialNormalized)
            return drainWasReady ? (Color32)readyColor : SegmentColorAt(drainSegments, a);
        return SegmentColorAt(segments, a);
    }

    private Color32 SegmentColorAt(List<(float end, bool barely)> list, float a)
    {
        foreach (var seg in list)
            if (a <= seg.end) return seg.barely ? barelyHitColor : normalHitColor;
        return normalHitColor;
    }

    private void AnimateRing(bool ready)
    {
        float scale = 1f;
        Color tint = Color.white;

        if (burstTimer > 0f)
        {
            // Pop out big and flash bright, then settle back.
            burstTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(burstTimer / burstDuration); // 0 -> 1
            scale = Mathf.Lerp(burstScale, 1f, t * t);
            tint = Color.Lerp(new Color(2f, 2f, 2f, 1f), Color.white, t);
        }
        else if (ready)
        {
            float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.5f + 0.5f;
            scale = 1f + pulse * pulseAmount;
            tint.a = Mathf.Lerp(0.7f, 1f, pulse);
        }

        ringRenderer.transform.localScale = Vector3.one * scale;
        ringRenderer.color = tint;
    }

    // ---------- Special ready popup ----------
    //
    // private void HandleSpecialReady()
    // {
    //     if (!player.HasSpecialGauge) return; // bots don't get the special effect
    //
    //     Vector3 pos = transform.position + (Vector3)popupOffset;
    //     GameObject popup;
    //
    //     if (popupPrefab != null && IsUIPrefab(popupPrefab))
    //     {
    //         // UI elements only draw inside a Canvas, so give it a small world-space one.
    //         popup = BuildWorldCanvas(pos);
    //         GameObject inst = Instantiate(popupPrefab, popup.transform, false);
    //         var rt = (RectTransform)inst.transform;
    //         rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
    //         rt.anchoredPosition = Vector2.zero;
    //         SetPopupText(inst);
    //     }
    //     else if (popupPrefab != null)
    //     {
    //         popup = Instantiate(popupPrefab, pos, Quaternion.identity);
    //         SetPopupText(popup);
    //     }
    //     else
    //     {
    //         popup = BuildPopup(pos);
    //     }
    //
    //     popup_text anim = popup.GetComponent<popup_text>();
    //     if (anim == null) anim = popup.AddComponent<popup_text>();
    //     anim.Play(popupLifetime, popupRise);
    // }
    //
    // private void SetPopupText(GameObject popup)
    // {
    //     TMP_Text text = popup.GetComponentInChildren<TMP_Text>();
    //     if (text != null && !string.IsNullOrEmpty(popupMessage)) text.text = popupMessage;
    // }
    //
    // /// <summary>True if the prefab is UI (e.g. "Text (TMP)" made under a Canvas) and has no Canvas of its own.</summary>
    // private static bool IsUIPrefab(GameObject prefab)
    // {
    //     return prefab.GetComponentInChildren<CanvasRenderer>(true) != null
    //         && prefab.GetComponentInChildren<Canvas>(true) == null;
    // }
    //
    // private GameObject BuildWorldCanvas(Vector3 pos)
    // {
    //     var root = new GameObject("SpecialReadyPopup", typeof(RectTransform));
    //     var canvas = root.AddComponent<Canvas>();
    //     canvas.renderMode = RenderMode.WorldSpace;
    //     if (TryGetComponent(out SpriteRenderer sr)) canvas.sortingLayerID = sr.sortingLayerID;
    //     canvas.sortingOrder = popupSortingOrder;
    //
    //     var rt = (RectTransform)root.transform;
    //     rt.position = pos;
    //     rt.sizeDelta = new Vector2(400f, 100f);
    //     rt.localScale = Vector3.one * popupCanvasScale;
    //     return root;
    // }
    //
    // private GameObject BuildPopup(Vector3 pos)
    // {
    //     var root = new GameObject("SpecialReadyPopup");
    //     root.transform.position = pos;
    //
    //     // Text
    //     var textObj = new GameObject("Text");
    //     textObj.transform.SetParent(root.transform, false);
    //     var tmp = textObj.AddComponent<TextMeshPro>();
    //     if (popupFont != null) tmp.font = popupFont;
    //     tmp.text = popupMessage;
    //     tmp.fontSize = popupFontSize;
    //     tmp.color = popupTextColor;
    //     tmp.alignment = TextAlignmentOptions.Center;
    //     tmp.textWrappingMode = TextWrappingModes.NoWrap;
    //     tmp.rectTransform.sizeDelta = new Vector2(20f, 5f);
    //     tmp.sortingOrder = popupSortingOrder + 1;
    //     tmp.ForceMeshUpdate();
    //
    //     // Text box behind it, sized to the text
    //     Vector2 textSize = tmp.GetRenderedValues(false);
    //     var boxObj = new GameObject("Box");
    //     boxObj.transform.SetParent(root.transform, false);
    //     var box = boxObj.AddComponent<SpriteRenderer>();
    //     box.sprite = GetBoxSprite();
    //     box.drawMode = SpriteDrawMode.Sliced;
    //     box.size = textSize + popupPadding * 2f;
    //     box.sortingOrder = popupSortingOrder;
    //
    //     return root;
    // }
    //
    // /// <summary>Tiny 9-sliced pixel box: 1px border, clipped corners, stretchable middle.</summary>
    // private Sprite GetBoxSprite()
    // {
    //     Color32 fill = popupBoxColor, border = popupBorderColor;
    //     if (boxSprite != null && boxSpriteFill.Equals(fill) && boxSpriteBorder.Equals(border))
    //         return boxSprite;
    //
    //     const int size = 6;
    //     var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
    //     {
    //         filterMode = FilterMode.Point,
    //         wrapMode = TextureWrapMode.Clamp,
    //         name = "PopupBox"
    //     };
    //
    //     Color32 clear = new Color32(0, 0, 0, 0);
    //     for (int y = 0; y < size; y++)
    //     {
    //         for (int x = 0; x < size; x++)
    //         {
    //             bool edgeX = x == 0 || x == size - 1;
    //             bool edgeY = y == 0 || y == size - 1;
    //             Color32 c = edgeX && edgeY ? clear : (edgeX || edgeY ? border : fill);
    //             tex.SetPixel(x, y, c);
    //         }
    //     }
    //     tex.Apply();
    //
    //     float ppu = 1f / Mathf.Max(0.001f, popupBoxPixelSize);
    //     boxSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu,
    //                               0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
    //     boxSpriteFill = fill;
    //     boxSpriteBorder = border;
    //     return boxSprite;
    // }
}
