using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2.5D ping pong ball.
///
/// This object's position is the ball's spot ON THE TABLE (where its drop shadow is).
/// The ball sprite is lifted up by the ball's height, so when hit it arcs into the air,
/// comes back down and smacks into the table while the shadow slides along underneath it.
///
/// Setup: just put this on an object with the ball's SpriteRenderer. On play it automatically
/// moves the sprite onto a "Visual" child and creates a "Shadow" child. (Or make those
/// children yourself and assign them below.)
/// </summary>
public class ball_controller : MonoBehaviour
{
    public enum BallState { Held, InPlay, Dead }

    [Header("References (auto-created if empty)")]
    [Tooltip("The ball sprite (child). Lifted up by the ball's height.")]
    [SerializeField] private Transform ballVisual;
    [Tooltip("The drop shadow (child). Stays on the table and shrinks/fades as the ball goes higher.")]
    [SerializeField] private Transform shadow;
    [Tooltip("Collider covering the table's top surface (the Table's BoxCollider2D works). " +
             "If empty, Table Area below is used.")]
    [SerializeField] private Collider2D tableSurface;
    [SerializeField] private Rect tableArea = new Rect(-6f, -6f, 12f, 4f);

    [Header("Physics")]
    [SerializeField] private float gravity = 25f;
    [Tooltip("How much bounce speed is kept each time the ball hits the table (0-1).")]
    [Range(0f, 1f)]
    [SerializeField] private float bounciness = 0.8f;
    [Tooltip("World units the sprite moves up on screen per 1 unit of ball height.")]
    [SerializeField] private float heightToScreen = 1f;
    [SerializeField] private float netHeight = 0.5f;
    [Tooltip("Extra height shots try to clear the net by.")]
    [SerializeField] private float netClearance = 0.3f;
    [Tooltip("How far below the table the floor is. Ball is dead once it falls this far off the table.")]
    [SerializeField] private float floorDepth = 2f;

    [Header("Shot Autocorrect (hidden assist)")]
    [Tooltip("Shots landing closer than this to the table's top/bottom edges get pulled back toward the middle.")]
    [SerializeField] private float sideEdgeMargin = 1f;
    [Tooltip("Shots landing closer than this to the far end of the table get pulled back in.")]
    [SerializeField] private float endEdgeMargin = 1f;
    [Tooltip("Shots landing closer than this to the net get pushed further out.")]
    [SerializeField] private float netMargin = 1f;
    [Tooltip("1 = always fully corrected into the safe zone, 0 = no assist.")]
    [Range(0f, 1f)]
    [SerializeField] private float autocorrectStrength = 1f;

    [Header("Drop Shadow")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.4f);
    [Tooltip("Height at which the shadow reaches its smallest / faintest.")]
    [SerializeField] private float shadowShrinkHeight = 4f;
    [Range(0f, 1f)]
    [SerializeField] private float shadowMinScale = 0.5f;
    [Range(0f, 1f)]
    [SerializeField] private float shadowMinAlpha = 0.4f;

    [Header("Table Impact")]
    [Tooltip("How much the ball squashes when it hits the table (0 = none).")]
    [Range(0f, 0.8f)]
    [SerializeField] private float squashAmount = 0.35f;
    [SerializeField] private float squashDuration = 0.12f;

    [Header("Serving")]
    [Tooltip("Who serves first. If empty, the first player that registers serves.")]
    [SerializeField] private player_movemen firstServer;
    [SerializeField] private float serveHoldHeight = 1f;
    [Tooltip("Seconds after a point ends before the next serve.")]
    [SerializeField] private float pointResetDelay = 1f;

    /// <summary>Fires when a point ends. Argument is the winner (can be null if there's no opponent).</summary>
    public event Action<player_movemen> OnPointScored;
    /// <summary>Fires when the ball hits the table. Argument is the impact speed.</summary>
    public event Action<float> OnBounce;
    public event Action<player_movemen> OnHit;

    public BallState State { get; private set; } = BallState.Held;
    public player_movemen Server { get; private set; }
    public player_movemen LastHitter { get; private set; }

    /// <summary>The ball's contact point on the table plane (same as the shadow).</summary>
    public Vector2 GroundPosition => transform.position;
    /// <summary>Where the ball sprite's center appears on screen (lifted up from the shadow by its height).</summary>
    public Vector2 VisualPosition => ToVisual(GroundPosition, height);
    /// <summary>Height above the table. Negative = fallen below table level.</summary>
    public float Height => height;
    public Vector2 GroundVelocity => groundVelocity;
    public float NetX => TableBounds.center.x;

    public Bounds TableBounds =>
        tableSurface != null
            ? tableSurface.bounds
            : new Bounds(tableArea.center, new Vector3(tableArea.width, tableArea.height, 0f));

    private readonly List<player_movemen> players = new List<player_movemen>();
    private Vector2 groundVelocity;
    private float height;
    private float verticalVelocity;
    private int receiverBounces; // bounces on the opponent's side since the last hit
    private float resetTimer;

    private SpriteRenderer shadowRenderer;
    private Vector3 shadowBaseScale = Vector3.one;
    private Vector3 visualBaseScale = Vector3.one;
    private float ballRadius;   // half the ball sprite's height, so its bottom touches the table at height 0
    private float squashTimer;

    private void Awake()
    {
        SetUpVisual();
        SetUpShadow();
    }

    public void RegisterPlayer(player_movemen player)
    {
        if (!players.Contains(player)) players.Add(player);
    }

    private void Update()
    {
        if (State == BallState.Held)
        {
            if (Server == null) Server = firstServer != null ? firstServer : (players.Count > 0 ? players[0] : null);
            FollowServer();
        }
        else
        {
            Simulate(Time.deltaTime);

            if (State == BallState.Dead)
            {
                resetTimer -= Time.deltaTime;
                if (resetTimer <= 0f) PrepareServe(Server);
            }
        }

        UpdateVisuals();
    }

    // ---------- Hitting ----------

    /// <summary>
    /// Pops the ball up into an arc that peaks at <paramref name="arcHeight"/> above the table
    /// and comes down onto <paramref name="target"/> (a point on the table).
    /// The arc is raised automatically if it wouldn't clear the net.
    /// </summary>
    public void Hit(player_movemen hitter, Vector2 target, float arcHeight)
    {
        LastHitter = hitter;
        receiverBounces = 0;
        State = BallState.InPlay;

        Vector2 from = GroundPosition;
        target = AutocorrectTarget(target, hitter);
        float apex = Mathf.Max(arcHeight, height + 0.05f, netHeight + netClearance);

        for (int i = 0; i < 10; i++)
        {
            // Rise from current height to the apex, then fall from the apex onto the table.
            float upSpeed = Mathf.Sqrt(2f * gravity * Mathf.Max(0f, apex - height));
            float timeUp = upSpeed / gravity;
            float timeDown = Mathf.Sqrt(2f * apex / gravity);

            verticalVelocity = upSpeed;
            groundVelocity = (target - from) / (timeUp + timeDown);

            if (ClearsNet(from)) break;
            apex *= 1.15f; // too flat, go higher
        }

        OnHit?.Invoke(hitter);
    }

    /// <summary>
    /// Hidden assist: if a shot would land too close to an edge of the opponent's half
    /// (sides, far end, or right by the net), nudge the landing spot back toward the middle.
    /// </summary>
    private Vector2 AutocorrectTarget(Vector2 target, player_movemen hitter)
    {
        if (autocorrectStrength <= 0f) return target;

        Bounds b = TableBounds;
        float netX = NetX;
        int toward = hitter != null ? hitter.Facing : (target.x >= netX ? 1 : -1);

        // Safe landing zone on the receiving half.
        float nearX = netX + toward * netMargin;
        float farX = (toward > 0 ? b.max.x : b.min.x) - toward * endEdgeMargin;
        float minX = Mathf.Min(nearX, farX), maxX = Mathf.Max(nearX, farX);
        float minY = b.min.y + sideEdgeMargin, maxY = b.max.y - sideEdgeMargin;
        if (minY > maxY) minY = maxY = b.center.y;

        Vector2 safe = new Vector2(Mathf.Clamp(target.x, minX, maxX), Mathf.Clamp(target.y, minY, maxY));
        return Vector2.Lerp(target, safe, autocorrectStrength);
    }

    private bool ClearsNet(Vector2 from)
    {
        if (Mathf.Approximately(groundVelocity.x, 0f)) return true;
        float tNet = (NetX - from.x) / groundVelocity.x;
        if (tNet <= 0f) return true; // not heading over the net
        float hNet = height + verticalVelocity * tNet - 0.5f * gravity * tNet * tNet;
        return hNet >= netHeight + netClearance;
    }

    // ---------- Simulation ----------

    private void Simulate(float dt)
    {
        Vector2 prevPos = GroundPosition;
        float prevHeight = height;

        Vector2 newPos = prevPos + groundVelocity * dt;
        verticalVelocity -= gravity * dt;
        height += verticalVelocity * dt;
        transform.position = new Vector3(newPos.x, newPos.y, transform.position.z);

        if (State == BallState.InPlay && HitNet(prevPos, newPos))
            return;

        // Only hit the table when coming down onto it from above (not when sliding in from under it).
        if (IsOverTable(newPos) && prevHeight >= 0f && height <= 0f && verticalVelocity < 0f)
        {
            BounceOnTable(newPos);
        }
        else if (height <= -floorDepth)
        {
            HitFloor();
        }
    }

    private bool HitNet(Vector2 prevPos, Vector2 newPos)
    {
        float netX = NetX;
        bool crossed = Mathf.Sign(prevPos.x - netX) != Mathf.Sign(newPos.x - netX);
        if (!crossed || height >= netHeight || !IsWithinTableDepth(newPos.y)) return false;

        // Knock it back onto the hitter's side and let it drop.
        transform.position = new Vector3(prevPos.x, prevPos.y, transform.position.z);
        groundVelocity = new Vector2(-groundVelocity.x * 0.15f, groundVelocity.y * 0.3f);
        verticalVelocity = Mathf.Min(verticalVelocity, 0f);

        EndPoint(Opponent(LastHitter)); // hitter put it into the net
        return true;
    }

    private void BounceOnTable(Vector2 pos)
    {
        float impactSpeed = -verticalVelocity;

        height = 0f;
        verticalVelocity = impactSpeed * bounciness;
        if (verticalVelocity < 0.5f) verticalVelocity = 0f; // settle instead of jittering
        else squashTimer = squashDuration;                  // visible smack on real impacts

        OnBounce?.Invoke(impactSpeed);

        if (State != BallState.InPlay || LastHitter == null) return;

        int side = pos.x < NetX ? -1 : 1;
        if (side == LastHitter.Side)
        {
            EndPoint(Opponent(LastHitter)); // bounced on the hitter's own side
        }
        else if (++receiverBounces >= 2)
        {
            EndPoint(LastHitter); // bounced twice, opponent never returned it
        }
    }

    private void HitFloor()
    {
        height = -floorDepth;
        verticalVelocity = 0f;
        groundVelocity = Vector2.zero;

        if (State != BallState.InPlay) return;

        // Landed off the table without touching the opponent's side = hitter's fault.
        // Bounced on the opponent's side first = opponent missed it.
        EndPoint(receiverBounces == 0 ? Opponent(LastHitter) : LastHitter);
    }

    // ---------- Points / serving ----------

    private void EndPoint(player_movemen winner)
    {
        State = BallState.Dead;
        resetTimer = pointResetDelay;

        if (winner != null)
        {
            winner.AddPoint();
            Server = winner; // winner serves next
        }

        OnPointScored?.Invoke(winner);
    }

    /// <summary>Puts the ball on <paramref name="server"/>'s paddle, waiting for them to hit.</summary>
    public void PrepareServe(player_movemen server)
    {
        Server = server;
        LastHitter = null;
        receiverBounces = 0;
        groundVelocity = Vector2.zero;
        verticalVelocity = 0f;
        State = BallState.Held;
        FollowServer();
    }

    private void FollowServer()
    {
        height = serveHoldHeight;
        if (Server == null) return;
        Vector2 p = Server.PaddlePoint;
        transform.position = new Vector3(p.x, p.y, transform.position.z);
    }

    private player_movemen Opponent(player_movemen p)
    {
        foreach (player_movemen other in players)
            if (other != p) return other;
        return null;
    }

    // ---------- Prediction ----------

    /// <summary>
    /// Simulates ahead (including table bounces) to find where the ball will LOOK like it is
    /// when its shadow reaches world X = <paramref name="x"/>. Returns false if it isn't heading there.
    /// </summary>
    public bool PredictVisualAtX(float x, out Vector2 visual, out float time, float maxTime = 3f)
    {
        visual = VisualPosition;
        time = 0f;

        Vector2 pos = GroundPosition;
        Vector2 v = groundVelocity;
        float h = height, vy = verticalVelocity;
        if (Mathf.Abs(v.x) < 0.01f || Mathf.Sign(x - pos.x) != Mathf.Sign(v.x)) return false;

        const float step = 1f / 120f;
        for (float t = step; t <= maxTime; t += step)
        {
            float prevH = h;
            pos += v * step;
            vy -= gravity * step;
            h += vy * step;

            // Same bounce rules as Simulate().
            if (IsOverTable(pos) && prevH >= 0f && h <= 0f && vy < 0f)
            {
                h = 0f;
                vy = -vy * bounciness;
                if (vy < 0.5f) vy = 0f;
            }
            else if (h <= -floorDepth)
            {
                h = -floorDepth;
                vy = 0f;
            }

            if ((v.x > 0f && pos.x >= x) || (v.x < 0f && pos.x <= x))
            {
                visual = ToVisual(pos, h);
                time = t;
                return true;
            }
        }
        return false;
    }

    private Vector2 ToVisual(Vector2 ground, float h) => ground + new Vector2(0f, h * heightToScreen + ballRadius);

    // ---------- Visuals ----------

    private void UpdateVisuals()
    {
        if (ballVisual != null)
        {
            // Squash flat on impact, then spring back.
            float s = 0f;
            if (squashTimer > 0f)
            {
                squashTimer -= Time.deltaTime;
                s = squashAmount * Mathf.Clamp01(squashTimer / squashDuration);
            }
            ballVisual.localScale = Vector3.Scale(visualBaseScale, new Vector3(1f + s, 1f - s, 1f));

            // Lift the sprite in world space (so the object's scale doesn't exaggerate it).
            // ballRadius keeps the bottom of the ball resting on the shadow at height 0, even while squashed.
            float lift = height * heightToScreen + ballRadius * (1f - s);
            ballVisual.position = transform.position + new Vector3(0f, lift, 0f);
        }

        if (shadow != null)
        {
            // Shadow only shows while the ball is above the table top.
            bool show = height >= 0f && IsOverTable(GroundPosition);
            float t = shadowShrinkHeight > 0f ? Mathf.Clamp01(height / shadowShrinkHeight) : 0f;

            shadow.localScale = shadowBaseScale * Mathf.Lerp(1f, shadowMinScale, t);

            if (shadowRenderer != null)
            {
                shadowRenderer.enabled = show;
                Color c = shadowColor;
                c.a *= Mathf.Lerp(1f, shadowMinAlpha, t);
                shadowRenderer.color = c;
            }
        }
    }

    /// <summary>If no visual is assigned, moves this object's sprite onto a "Visual" child so it can be lifted.</summary>
    private void SetUpVisual()
    {
        if (ballVisual == null && TryGetComponent(out SpriteRenderer rootRenderer))
        {
            var visualObj = new GameObject("Visual");
            visualObj.transform.SetParent(transform, false);

            var sr = visualObj.AddComponent<SpriteRenderer>();
            sr.sprite = rootRenderer.sprite;
            sr.color = rootRenderer.color;
            sr.flipX = rootRenderer.flipX;
            sr.flipY = rootRenderer.flipY;
            sr.sharedMaterial = rootRenderer.sharedMaterial;
            sr.sortingLayerID = rootRenderer.sortingLayerID;
            sr.sortingOrder = rootRenderer.sortingOrder + 1; // above the shadow

            rootRenderer.enabled = false;
            ballVisual = visualObj.transform;
        }

        if (ballVisual == null)
        {
            Debug.LogWarning($"[ball_controller] No sprite found on '{name}'. Add a SpriteRenderer or assign Ball Visual.", this);
            return;
        }

        visualBaseScale = ballVisual.localScale;
        var visualRenderer = ballVisual.GetComponent<SpriteRenderer>();
        ballRadius = visualRenderer != null ? visualRenderer.bounds.extents.y : 0f;
    }

    /// <summary>If no shadow is assigned, creates a pixel-art ellipse shadow sized to match the ball.</summary>
    private void SetUpShadow()
    {
        if (shadow == null)
        {
            var visualRenderer = ballVisual != null ? ballVisual.GetComponent<SpriteRenderer>() : null;
            Sprite ballSprite = visualRenderer != null ? visualRenderer.sprite : null;

            int width = ballSprite != null ? Mathf.Max(4, Mathf.RoundToInt(ballSprite.rect.width)) : 8;
            float ppu = ballSprite != null ? ballSprite.pixelsPerUnit : 100f;

            var shadowObj = new GameObject("Shadow");
            shadowObj.transform.SetParent(transform, false);

            var sr = shadowObj.AddComponent<SpriteRenderer>();
            sr.sprite = CreateEllipseSprite(width, Mathf.Max(2, width / 2), ppu);
            if (visualRenderer != null)
            {
                sr.sortingLayerID = visualRenderer.sortingLayerID;
                sr.sortingOrder = visualRenderer.sortingOrder - 1; // under the ball, over the table
            }

            shadow = shadowObj.transform;
        }

        shadowRenderer = shadow.GetComponent<SpriteRenderer>();
        shadowBaseScale = shadow.localScale;
    }

    private static Sprite CreateEllipseSprite(int width, int height, float pixelsPerUnit)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "BallShadow"
        };

        float rx = width / 2f, ry = height / 2f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x + 0.5f - rx) / rx;
                float dy = (y + 0.5f - ry) / ry;
                tex.SetPixel(x, y, dx * dx + dy * dy <= 1f ? Color.white : Color.clear);
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }

    // ---------- Helpers ----------

    private bool IsOverTable(Vector2 pos)
    {
        Bounds b = TableBounds;
        return pos.x >= b.min.x && pos.x <= b.max.x && pos.y >= b.min.y && pos.y <= b.max.y;
    }

    private bool IsWithinTableDepth(float y)
    {
        Bounds b = TableBounds;
        return y >= b.min.y && y <= b.max.y;
    }

    private void OnDrawGizmos()
    {
        Bounds b = TableBounds;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(b.center, b.size);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(new Vector3(b.center.x, b.min.y), new Vector3(b.center.x, b.max.y));

        // Autocorrect safe landing zones (yellow), one per half.
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        float h = Mathf.Max(0f, b.size.y - 2f * sideEdgeMargin);
        float w = Mathf.Max(0f, b.extents.x - netMargin - endEdgeMargin);
        float offset = netMargin + w / 2f;
        Gizmos.DrawWireCube(new Vector3(b.center.x - offset, b.center.y), new Vector3(w, h));
        Gizmos.DrawWireCube(new Vector3(b.center.x + offset, b.center.y), new Vector3(w, h));
    }
}
