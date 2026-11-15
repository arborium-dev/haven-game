using UnityEngine;

/// Match-wide effects:
///  - Full time stop while a special's cutscene plays (used by special_ability).
///  - Plays an "impact" animation at the ball when it hits the table (after a special, or every bounce).
///  - The first table bounce after a special leaves a subtle scorch mark that stays for the rest of the match.
///
/// Optional to place in the scene (to tweak settings). If it's missing, one is created automatically.
public class match_effects : MonoBehaviour
{
    private static match_effects instance;

    public static match_effects Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<match_effects>();
                if (instance == null) instance = new GameObject("match_effects").AddComponent<match_effects>();
            }
            return instance;
        }
    }

    [Header("References (found automatically if empty)")]
    [SerializeField] private ball_controller ball;

    [Header("Impact Animation")]
    [Tooltip("Spawned at the ball when it hits the table. Should have an animation_controller with an animation " +
             "named Impact Animation (Loop off). It's destroyed when the animation finishes.")]
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private string impactAnimation = "impact";
    [Tooltip("OFF: only after a special has powered up the ball. ON: every table bounce.")]
    [SerializeField] private bool impactOnEveryBounce = false;
    [SerializeField] private Vector2 impactOffset = Vector2.zero;
    [Tooltip("Scale the effect by the ball's scale so its pixels match the ball's pixels.")]
    [SerializeField] private bool matchBallScale = true;
    [Tooltip("Draw the effect in front of the ball.")]
    [SerializeField] private bool drawInFrontOfBall = true;
    [Tooltip("Used if the impact animation's Frames Per Second is 0 (which would freeze it on frame 1).")]
    [SerializeField] private float fallbackFps = 12f;
    [Tooltip("Safety: destroy the impact object after this long even if its animation never finishes.")]
    [SerializeField] private float impactMaxLifetime = 2f;

    [Header("Table Mark (first bounce after a special)")]
    [Tooltip("Optional. Your own mark sprite/prefab. If empty, a pixel scorch mark is generated.")]
    [SerializeField] private GameObject tableMarkPrefab;
    [SerializeField] private Color markColor = Color.black;
    [Tooltip("How see-through the mark is (subtle = low).")]
    [Range(0f, 1f)]
    [SerializeField] private float markOpacity = 0.3f;
    [Tooltip("Size of the generated mark in pixels (matches the ball's pixel size).")]
    [SerializeField] private Vector2Int markPixelSize = new Vector2Int(14, 6);

    /// True while a special has time completely stopped.
    public bool TimeStopped { get; private set; }

    private ball_controller subscribedBall;
    private bool warnedNoImpactPrefab;
    private bool markPending;
    private Transform markContainer;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void Start() => HookBall();

    private void Update() => HookBall(); // picks up the ball if it's created later

    private void OnDestroy()
    {
        if (subscribedBall != null) subscribedBall.OnBounce -= HandleBounce;
        if (instance == this)
        {
            instance = null;
            Time.timeScale = 1f;
        }
    }

    // ---------- Time stop (special cutscene) ----------

    public void StopTime()
    {
        TimeStopped = true;
        Time.timeScale = 0f;
    }

    public void ResumeTime()
    {
        TimeStopped = false;
        Time.timeScale = 1f;
    }

    /// Back to normal time immediately (used when a point / match resets).
    public void ResetTime()
    {
        ResumeTime();
        markPending = false; // a special whose ball never bounced doesn't carry its mark into the next point
    }

    // ---------- Table marks ----------

    /// The next table bounce will leave a mark. Called when a special finishes.
    public void QueueTableMark() => markPending = true;

    /// Removes every mark from the table (used when a whole new match starts).
    public void ClearTableMarks()
    {
        markPending = false;
        if (markContainer == null) return;
        for (int i = markContainer.childCount - 1; i >= 0; i--)
            Destroy(markContainer.GetChild(i).gameObject);
    }

    private void SpawnTableMark(Vector2 pos)
    {
        if (markContainer == null)
        {
            markContainer = new GameObject("TableMarks").transform;
            markContainer.SetParent(transform, false);
        }

        // Draw on the table, just under the ball's shadow (same order, pushed slightly back).
        SpriteRenderer shadow = ball.ShadowRenderer;
        int layer = shadow != null ? shadow.sortingLayerID : 0;
        int order = shadow != null ? shadow.sortingOrder : 1;
        var spawnPos = new Vector3(pos.x, pos.y, ball.transform.position.z + 0.01f);

        if (tableMarkPrefab != null)
        {
            GameObject custom = Instantiate(tableMarkPrefab, spawnPos, Quaternion.identity, markContainer);
            foreach (SpriteRenderer r in custom.GetComponentsInChildren<SpriteRenderer>())
            {
                r.sortingLayerID = layer;
                r.sortingOrder = order;
            }
            return;
        }

        // Match the ball's pixel size so the mark looks like part of the same pixel art.
        SpriteRenderer ballSprite = ball.VisualRenderer;
        float ppu = ballSprite != null && ballSprite.sprite != null ? ballSprite.sprite.pixelsPerUnit : 100f;

        var mark = new GameObject("TableMark");
        mark.transform.SetParent(markContainer, false);
        mark.transform.position = spawnPos;
        mark.transform.localScale = ball.transform.lossyScale;

        var sr = mark.AddComponent<SpriteRenderer>();
        sr.sprite = CreateScorchSprite(markPixelSize.x, markPixelSize.y, ppu);
        Color c = markColor;
        c.a = markOpacity;
        sr.color = c;
        sr.sortingLayerID = layer;
        sr.sortingOrder = order;
    }

    /// A squashed, ragged pixel blob with a few soot specks around it. Every mark is a bit different.
    private static Sprite CreateScorchSprite(int w, int h, float ppu)
    {
        w = Mathf.Max(4, w);
        h = Mathf.Max(2, h);
        int pad = Mathf.Max(2, w / 4); // room for specks
        int tw = w + pad * 2, th = h + pad;

        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "TableMark"
        };

        float cx = tw / 2f, cy = th / 2f, rx = w / 2f, ry = h / 2f;
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                float dx = (x + 0.5f - cx) / rx;
                float dy = (y + 0.5f - cy) / ry;
                float d = Mathf.Sqrt(dx * dx + dy * dy) + Random.Range(-0.25f, 0.15f); // ragged edge

                float a = 0f;
                if (d < 0.55f) a = 1f;                                 // dark core
                else if (d < 1f) a = 0.6f;                             // lighter rim
                else if (d < 1.6f && Random.value < 0.07f) a = 0.45f;  // scattered soot specks

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a)); // tinted by the renderer's color
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), ppu);
    }

    // ---------- Impact animation ----------

    private void HookBall()
    {
        if (ball == null)
        {
            // The real ball, never a tutorial practice copy.
            foreach (ball_controller b in FindObjectsByType<ball_controller>())
                if (!b.PracticeMode) { ball = b; break; }
        }
        if (ball == subscribedBall) return;

        if (subscribedBall != null) subscribedBall.OnBounce -= HandleBounce;
        subscribedBall = ball;
        if (subscribedBall != null) subscribedBall.OnBounce += HandleBounce;
    }

    private void HandleBounce(float impactSpeed)
    {
        if (ball.State != ball_controller.BallState.InPlay) return; // ignore the little settling bounces after a point

        if (markPending)
        {
            markPending = false; // only the first bounce after a special
            SpawnTableMark(ball.GroundPosition);
        }

        if (ball.IsPowered || impactOnEveryBounce)
            SpawnImpact(ball.VisualPosition + impactOffset);
    }
// this code is some bs
    private void SpawnImpact(Vector2 pos)
    {
        if (impactPrefab == null)
        {
            if (!warnedNoImpactPrefab)
                Debug.LogWarning("[match_effects] No Impact Prefab assigned, so no impact animation will play.", this);
            warnedNoImpactPrefab = true;
            return;
        }

        GameObject fx = Instantiate(impactPrefab, new Vector3(pos.x, pos.y, ball.transform.position.z), Quaternion.identity);

        if (matchBallScale)
            fx.transform.localScale = Vector3.Scale(impactPrefab.transform.localScale, ball.transform.lossyScale);

        if (drawInFrontOfBall)
        {
            int ballOrder = int.MinValue;
            int ballLayer = 0;
            foreach (SpriteRenderer r in ball.GetComponentsInChildren<SpriteRenderer>())
            {
                if (r.sortingOrder > ballOrder)
                {
                    ballOrder = r.sortingOrder;
                    ballLayer = r.sortingLayerID;
                }
            }
            if (ballOrder != int.MinValue)
            {
                foreach (SpriteRenderer r in fx.GetComponentsInChildren<SpriteRenderer>())
                {
                    r.sortingLayerID = ballLayer;
                    r.sortingOrder = ballOrder + 1;
                }
            }
        }

        var anim = fx.GetComponentInChildren<animation_controller>();
        if (anim != null && anim.HasAnimation(impactAnimation))
        {
            if (anim.animations.Exists(a => a.name == impactAnimation && a.framesPerSecond <= 0f))
                anim.SetAnimationFPS(impactAnimation, fallbackFps);

            anim.Play(impactAnimation, restartIfSame: true);
            anim.OnAnimationFinished += _ => { if (fx != null) Destroy(fx); };
        }

        Destroy(fx, impactMaxLifetime);
    }
}
