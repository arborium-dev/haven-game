using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
// some people are really  REALLY bad at reading...
///   Phase 1 - MOVE:    shows the up/down keys. Done once the player has moved both up and down.
///   Phase 2 - HIT:     practice balls fly at the player, showing the hit key. Missed? Another one comes.
///                      Done after a successful hit.
///   Phase 3 - SPECIAL: more practice balls; "HIT MIDDLE, FILL FASTER". Done when the special gauge is full.
public class tutorial_manager : MonoBehaviour
{
    private static tutorial_manager instance;

    public static tutorial_manager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<tutorial_manager>();
                if (instance == null) instance = new GameObject("tutorial_manager").AddComponent<tutorial_manager>();
            }
            return instance;
        }
    }

    [Header("Phase 1: Move")]
    [Tooltip("Seconds the player must hold up, and hold down, to finish the move phase.")]
    [SerializeField] private float moveHoldNeeded = 0.3f;

    [Header("Phase 2: Hit")]
    [Tooltip("Successful hits needed to finish the hit phase.")]
    [SerializeField] private int hitsNeeded = 1;

    [Header("Practice Balls")]
    [Tooltip("Practice ball speed (1 = normal). A bit slow makes learning easier.")]
    [SerializeField] private float practiceSpeed = 0.8f;
    [Tooltip("Where practice balls land on the player's half: 0 = by the net, 1 = by the player's end.")]
    [SerializeField] private Vector2 landingDepthRange = new Vector2(0.55f, 0.85f);
    [Tooltip("Phase 2 aims balls within this distance (up/down) of the paddle, so they're easy to reach.")]
    [SerializeField] private float easyAimSpread = 0.6f;
    [Tooltip("Seconds between one practice ball finishing and the next one coming.")]
    [SerializeField] private float delayBetweenBalls = 0.6f;
    [Tooltip("Returned balls are removed when they reach the center of the screen. This is a safety limit in case " +
             "one never gets there (seconds after the hit).")]
    [SerializeField] private float returnedBallLifetime = 2f;
    [Tooltip("How high up the screen practice balls appear (0 = bottom, 0.5 = middle, 1 = top). " +
             "They only fall from there, so they never rise into the panels.")]
    [Range(0f, 1f)]
    [SerializeField] private float spawnScreenHeight = 0.5f;
    [Tooltip("How high above the table a practice ball starts (it falls from here). Higher = longer, gentler drop.")]
    [SerializeField] private float spawnDropHeight = 2.5f;
    [Tooltip("Practice balls always land at least this far lower on screen than where they appeared.")]
    [SerializeField] private float minimumDrop = 0.5f;
    [Tooltip("If the spawn point would be under a tutorial panel, it's moved down to stay this far below it.")]
    [SerializeField] private float panelClearance = 0.3f;

    [Header("Text")]
    [SerializeField] private string moveTitle = "MOVE";
    [SerializeField] private string hitTitle = "HIT";
    [SerializeField] private string specialTitle = "SPECIAL";
    [SerializeField] private string specialHint = "HIT MIDDLE, FILL FASTER";
    [SerializeField] private string phaseDoneTitle = "NICE!";
    [SerializeField] private string finishedTitle = "READY!";

    [Header("Look")]
    [Tooltip("Font for the panels. Empty = the same font as the score.")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color panelColor = new Color(0.16f, 0.06f, 0.23f, 0.85f);
    [SerializeField] private Color outlineColor = new Color32(0xF9, 0xB2, 0x1E, 0xFF);  // yellow
    [SerializeField] private Color titleColor = new Color32(0xF6, 0xE6, 0xC4, 0xFF);    // cream
    [SerializeField] private Color hintColor = new Color32(0xF9, 0xB2, 0x1E, 0xFF);     // yellow
    [SerializeField] private Color niceColor = new Color32(0xEC, 0x27, 0x6E, 0xFF);     // pink
    [Tooltip("How high up the screen the panels sit (0 = bottom, 1 = top).")]
    [Range(0f, 1f)]
    [SerializeField] private float panelHeight = 0.72f;
    [SerializeField] private Vector2 panelSize = new Vector2(400f, 160f);
    [Tooltip("Seconds the 'NICE!' shows between phases.")]
    [SerializeField] private float phaseDoneDelay = 0.7f;
    [Tooltip("Seconds for a finished player's panel to fade out.")]
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("See-Through When a Ball Is Behind")]
    [Tooltip("How visible a panel stays while a ball is behind it (0 = invisible, 1 = solid).")]
    [Range(0f, 1f)]
    [SerializeField] private float translucentAlpha = 0.3f;
    [Tooltip("Extra space around a panel (screen pixels) that still counts as 'behind' it.")]
    [SerializeField] private float seeThroughPadding = 40f;
    [Tooltip("Seconds to fade between solid and see-through.")]
    [SerializeField] private float seeThroughFadeTime = 0.12f;

    /// True while the tutorial is running (the real ball is waiting).
    public bool Running { get; private set; }

    private ball_controller ball;
    private Canvas canvas;
    private readonly List<Panel> panels = new List<Panel>();
    private readonly List<Practice> practices = new List<Practice>();

    private class Panel
    {
        public RectTransform root;
        public CanvasGroup group;
        public TMP_Text title, hint, step;
        public float fadeAlpha = 1f;  // goes to 0 when this player finishes
        public float seeThrough = 1f; // drops while a ball is behind the panel
    }

    /// Runs inside a single player's tutorial: their current practice ball and hit count.
    private class Practice
    {
        public player_movemen player;
        public bool active;
        public bool easyAim;
        public int hits;
        public ball_controller current;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// Holds the real ball back and starts the tutorial. Called by ball_controller at the start of the game.
    public void Begin(ball_controller realBall)
    {
        if (Running || realBall == null) return;
        Running = true;
        ball = realBall;
        ball.WaitingToStart = true;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return null; // let every player finish starting up (bots, sides, etc.)

        var humans = new List<player_movemen>();
        foreach (player_movemen p in FindObjectsByType<player_movemen>())
            if (p.Ball == ball && !p.IsBot) humans.Add(p);

        if (humans.Count > 0)
        {
            BuildCanvas();
            int remaining = humans.Count;
            foreach (player_movemen p in humans)
                StartCoroutine(RunFor(p, () => remaining--));

            while (remaining > 0) yield return null;
        }

        // Everyone's done: clean slate, then the real game begins.
        foreach (player_movemen p in humans)
        {
            p.SetPracticeBall(null);
            p.ResetSpecial();
        }
        if (canvas != null) Destroy(canvas.gameObject);
        panels.Clear();
        practices.Clear();

        ball.WaitingToStart = false;
        Running = false;
    }

    // ---------- One player's tutorial ----------

    private IEnumerator RunFor(player_movemen p, Action done)
    {
        Panel panel = BuildPanel(p.Side);

        // Phase 1: move up and down.
        Show(panel, moveTitle, $"{KeyName(p.UpKey)} / {KeyName(p.DownKey)}", "1/3");
        float up = 0f, down = 0f;
        while (up < moveHoldNeeded || down < moveHoldNeeded)
        {
            if (p.LastVertical > 0.1f) up += Time.deltaTime;
            else if (p.LastVertical < -0.1f) down += Time.deltaTime;
            yield return null;
        }
        yield return PhaseDone(panel);

        // Phase 2: hit an easy practice ball (another comes after each miss).
        Show(panel, hitTitle, $"PRESS {KeyName(p.HitKey)}", "2/3");
        var practice = new Practice { player = p, active = true, easyAim = true };
        practices.Add(practice);
        Coroutine loop = StartCoroutine(PracticeLoop(practice));
        while (practice.hits < hitsNeeded) yield return null;
        yield return PhaseDone(panel);

        // Phase 3: keep hitting until the special gauge is full.
        Show(panel, specialTitle, specialHint, "3/3");
        practice.easyAim = false;
        while (!p.SpecialIsFull) yield return null;

        // Finished: stop the balls and fade this player's panel away.
        practice.active = false;
        StopCoroutine(loop);
        if (practice.current != null) Destroy(practice.current.gameObject);
        p.SetPracticeBall(null);

        Show(panel, finishedTitle, "", "");
        panel.title.color = niceColor;
        yield return new WaitForSeconds(phaseDoneDelay);
        yield return Fade(panel, fadeDuration);

        done();
    }

    private IEnumerator PhaseDone(Panel panel)
    {
        Show(panel, phaseDoneTitle, panel.hint.text, panel.step.text);
        panel.title.color = niceColor;
        yield return new WaitForSeconds(phaseDoneDelay);
        panel.title.color = titleColor;
    }

    // ---------- Practice balls ----------

    /// Keeps sending practice balls at the player. Counts hits; misses just get another ball.
    private IEnumerator PracticeLoop(Practice s)
    {
        while (s.active)
        {
            ball_controller pb = ball.SpawnPracticeCopy();
            s.current = pb;
            s.player.SetPracticeBall(pb);

            bool hit = false, ended = false;
            pb.OnHit += hitter => { if (hitter == s.player) hit = true; };
            pb.OnPracticeEnded += _ => ended = true;
            Launch(pb, s.player, s.easyAim);

            while (!hit && !ended) yield return null;

            if (hit)
            {
                s.hits++;
                // Let the return fly off until it reaches the center of the screen, then remove it,
                // so one player's returns never end up in the other player's area.
                int side = s.player.Side;
                float t = 0f;
                while (!ended && t < returnedBallLifetime && pb != null
                       && (pb.GroundPosition.x - CenterX()) * side > 0f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
            }

            s.player.SetPracticeBall(null);
            if (pb != null) Destroy(pb.gameObject);
            s.current = null;

            yield return new WaitForSeconds(delayBetweenBalls);
        }
    }

    /// <summary>
    /// Drops a practice ball from the middle of the screen: it appears there, then only falls (never rises),
    /// travelling down onto the player's half of the table.
    /// </summary>
    private void Launch(ball_controller pb, player_movemen p, bool easyAim)
    {
        Bounds table = ball.TableBounds;
        int side = p.Side; // -1 = player is on the left
        float lowestY = table.min.y + 0.3f;
        float radius = pb.VisualPosition.y - pb.GroundPosition.y - pb.Height;

        // Where the ball appears on screen: the middle (kept just under the panels if they'd overlap it).
        float startY = Mathf.Min(ViewportToWorldY(spawnScreenHeight), PanelBottomWorldY() - panelClearance - 2f * radius);

        // It starts high above a spot on the table so that it SHOWS at startY, then falls from there.
        float fromHeight = spawnDropHeight;
        float fromY = startY - fromHeight - radius;
        if (fromY < lowestY)
        {
            fromY = lowestY;
            fromHeight = Mathf.Max(0.5f, startY - radius - fromY);
        }
        float fromX = CenterX() + side * 0.1f; // nudged onto this player's side so it never crosses over

        // Lands on the player's half, lower on screen than where it appeared, so it travels downward.
        float depth = UnityEngine.Random.Range(landingDepthRange.x, landingDepthRange.y);
        float targetX = ball.LandingXAtDepth(side, depth);
        float targetY = easyAim
            ? p.PaddlePoint.y + UnityEngine.Random.Range(-easyAimSpread, easyAimSpread)
            : UnityEngine.Random.Range(table.min.y, table.max.y); // (autocorrect keeps it off the edges)
        targetY = Mathf.Clamp(targetY, lowestY, Mathf.Max(lowestY, startY - minimumDrop));

        // Arc = just above where it starts, so it never rises: it only drops.
        float arc = fromHeight + 0.05f;
        pb.LaunchPractice(new Vector2(fromX, fromY), fromHeight, new Vector2(targetX, targetY), arc, practiceSpeed);
    }

    /// <summary>World Y of the lowest visible tutorial panel's bottom edge (infinity if none are showing).</summary>
    private float PanelBottomWorldY()
    {
        Camera cam = Camera.main;
        float lowest = float.PositiveInfinity;
        if (cam == null) return lowest;

        var corners = new Vector3[4];
        foreach (Panel panel in panels)
        {
            if (panel.root == null || panel.fadeAlpha <= 0f) continue;
            GetScreenCorners(panel.root, corners);
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(corners[0].x, corners[0].y, Mathf.Abs(cam.transform.position.z)));
            lowest = Mathf.Min(lowest, world.y);
        }
        return lowest;
    }

    /// <summary>World Y at a height on screen (0 = bottom, 1 = top).</summary>
    private float ViewportToWorldY(float viewportY)
    {
        Camera cam = Camera.main;
        if (cam == null) return ball.TableBounds.center.y;
        return cam.ViewportToWorldPoint(new Vector3(0.5f, viewportY, Mathf.Abs(cam.transform.position.z))).y;
    }

    /// The middle of the screen in world X (falls back to the net if there's no camera).
    private float CenterX()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform.position.x : ball.NetX;
    }

    // ---------- UI ----------

    private void BuildCanvas()
    {
        if (font == null)
        {
            TMP_Text anyText = FindAnyObjectByType<TMP_Text>();
            if (anyText != null) font = anyText.font;
        }

        var obj = new GameObject("TutorialUI");
        canvas = obj.AddComponent<Canvas>();
        Camera cam = Camera.main;
        if (cam != null)
        {
            // On the game camera, so the panels stay inside the 16:9 area (see aspect_lock).
            aspect_lock.AttachToCamera(canvas, cam, 50);
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // under the wave transition
        }

        // Scales with resolution; "Shrink" keeps the 1280x720 layout fully inside the 16:9 area.
        var scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
    }

    /// <summary>A panel's corners in screen pixels, whichever way the tutorial canvas is drawn.</summary>
    private void GetScreenCorners(RectTransform rt, Vector3[] corners)
    {
        rt.GetWorldCorners(corners);
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.worldCamera == null) return;
        for (int i = 0; i < corners.Length; i++)
            corners[i] = canvas.worldCamera.WorldToScreenPoint(corners[i]);
    }

    private Panel BuildPanel(int side)
    {
        var panel = new Panel();
        panels.Add(panel);

        var obj = new GameObject(side < 0 ? "Left Tutorial" : "Right Tutorial", typeof(RectTransform));
        obj.transform.SetParent(canvas.transform, false);
        panel.root = (RectTransform)obj.transform;
        Vector2 anchor = new Vector2(side < 0 ? 0.25f : 0.75f, panelHeight);
        panel.root.anchorMin = panel.root.anchorMax = anchor;
        panel.root.sizeDelta = panelSize;

        var bg = obj.AddComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;
        var outline = obj.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(4f, -4f);

        panel.group = obj.AddComponent<CanvasGroup>();
        panel.group.blocksRaycasts = false;

        panel.title = MakeText(obj.transform, 56f, titleColor, new Vector2(0f, 0.45f), new Vector2(1f, 1f), TextAlignmentOptions.Center);
        panel.hint = MakeText(obj.transform, 28f, hintColor, new Vector2(0f, 0.1f), new Vector2(1f, 0.45f), TextAlignmentOptions.Center);
        panel.step = MakeText(obj.transform, 18f, titleColor, new Vector2(0.75f, 0f), new Vector2(0.97f, 0.18f), TextAlignmentOptions.BottomRight);
        return panel;
    }

    private TMP_Text MakeText(Transform parent, float size, Color color, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions align)
    {
        var obj = new GameObject("Text", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(12f, 0f);
        rt.offsetMax = new Vector2(-12f, 0f);

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 10f;
        tmp.fontSizeMax = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private void Show(Panel panel, string title, string hint, string step)
    {
        panel.title.text = title;
        panel.hint.text = hint;
        panel.step.text = step;
        StartCoroutine(Pop(panel.root));
    }

    private static IEnumerator Pop(RectTransform rt)
    {
        const float duration = 0.15f;
        float t = 0f;
        while (t < duration && rt != null)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / duration);
            rt.localScale = Vector3.one * (1f + 0.15f * k);
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    private static IEnumerator Fade(Panel panel, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            panel.fadeAlpha = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
            yield return null;
        }
        panel.fadeAlpha = 0f;
    }

    /// Panels go see-through while any practice ball is behind them on screen, so you can track it.
    private void LateUpdate()
    {
        if (panels.Count == 0) return;

        Camera cam = Camera.main;
        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, seeThroughFadeTime);

        foreach (Panel panel in panels)
        {
            if (panel.root == null) continue;

            bool ballBehind = false;
            if (cam != null)
            {
                Rect area = ScreenRect(panel.root, seeThroughPadding);
                foreach (Practice p in practices)
                {
                    if (p.current == null) continue;
                    Vector3 screen = cam.WorldToScreenPoint(p.current.VisualPosition);
                    if (area.Contains(screen))
                    {
                        ballBehind = true;
                        break;
                    }
                }
            }

            panel.seeThrough = Mathf.MoveTowards(panel.seeThrough, ballBehind ? translucentAlpha : 1f, step);
            panel.group.alpha = panel.fadeAlpha * panel.seeThrough;
        }
    }

    /// A panel's area in screen pixels (the tutorial canvas is a screen overlay), grown by <paramref name="padding"/>.
    private Rect ScreenRect(RectTransform rt, float padding)
    {
        var corners = new Vector3[4];
        GetScreenCorners(rt, corners);
        return Rect.MinMaxRect(corners[0].x - padding, corners[0].y - padding,
                               corners[2].x + padding, corners[2].y + padding);
    }

    /// Friendly key names for the panels ("UP", "SPACE", "W"...).
    private static string KeyName(Key key)
    {
        switch (key)
        {
            case Key.UpArrow: return "UP";
            case Key.DownArrow: return "DOWN";
            case Key.LeftArrow: return "LEFT";
            case Key.RightArrow: return "RIGHT";
            case Key.Enter: return "ENTER";
            case Key.NumpadEnter: return "ENTER";
            case Key.Space: return "SPACE";
        }

        string name = key.ToString();
        if (name.StartsWith("Digit")) name = name.Substring(5);
        return name.ToUpperInvariant();
    }
}
