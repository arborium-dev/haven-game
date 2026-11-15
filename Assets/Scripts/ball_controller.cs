// ball

using System;
using System.Collections.Generic;
using UnityEngine;

/// 2.5D ping pong ball.
///
/// This object's position is the ball's spot ON THE TABLE (where its drop shadow is).
/// The ball sprite is lifted up by the ball's height, so when hit it arcs into the air,
/// comes back down and smacks into the table while the shadow slides along underneath it.
///
/// Setup: just put this on an object with the ball's SpriteRenderer. On play it automatically
/// moves the sprite onto a "Visual" child and creates a "Shadow" child. (Or make those
/// children yourself and assign them below.)
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

    [Header("Slow Hits")]
    [Tooltip("After this many slow (barely) hits in a row, counting both players, the next slow hit is too weak " +
             "to clear the net: it rolls back to the hitter, who has to roll-save it. 0 = never.")]
    [SerializeField] private int maxSlowHitsInARow = 3;
    [Tooltip("Chance (0-1) that ANY slow hit is too weak to clear the net and becomes a roll save for the hitter. " +
             "Special shots are never slow hits, so they're never affected.")]
    [Range(0f, 1f)]
    [SerializeField] private float slowHitRollChance = 0.1f;
    [Tooltip("How high that too-weak shot lobs before dropping into the net.")]
    [SerializeField] private float dribbleArcHeight = 1.2f;

    [Header("Wall Spin (keeps rallies moving)")]
    [Tooltip("The ball counts as 'at a wall' when it's within this distance of the table's top or bottom edge.")]
    [SerializeField] private float wallZone = 1.5f;
    [Tooltip("Seconds of play spent at a wall before spin kicks in.")]
    [SerializeField] private float wallTimeBeforeSpin = 3f;
    [Tooltip("How hard spin curves the ball back toward the middle (sideways pull per second). Keep it subtle.")]
    [SerializeField] private float spinStrength = 1.2f;
    [Tooltip("Seconds for spin to fade in once it starts.")]
    [SerializeField] private float spinRampTime = 1f;

    [Header("Keeping The Ball Reachable")]
    [Tooltip("Angle every shot (specials included) so its straight-line path stays on the table all the way to the " +
             "receiver's end, instead of drifting off a side after it lands.")]
    [SerializeField] private bool keepShotsOnTable = true;
    [Tooltip("How far in from the sides that path has to stay.")]
    [SerializeField] private float pathSideMargin = 0.5f;
    [Tooltip("A ball sliding/rolling along the table this close to a side edge gets pushed away from it.")]
    [SerializeField] private float wallSlideMargin = 0.4f;
    [Tooltip("How fast a ball slides away from a side edge (at least).")]
    [SerializeField] private float slideAwaySpeed = 1f;

    [Header("Drop Shadow")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.4f);
    [Tooltip("Height at which the shadow reaches its smallest / faintest.")]
    [SerializeField] private float shadowShrinkHeight = 4f;
    [Range(0f, 1f)]
    [SerializeField] private float shadowMinScale = 0.5f;
    [Range(0f, 1f)]
    [SerializeField] private float shadowMinAlpha = 0.4f;

    [Header("Table Bounce Sound (leave empty for no sound)")]
    [Tooltip("Played when the ball hits the table.")]
    [SerializeField] private AudioClip tableBounceSound;
    [Range(0f, 1f)]
    [SerializeField] private float tableBounceVolume = 1f;
    [Tooltip("Bounces softer than this are silent, so a rolling / settling ball doesn't buzz.")]
    [SerializeField] private float minBounceSpeedForSound = 1f;

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

    [Header("Winning")]
    [Tooltip("First player to this many points wins the round (0 = play forever).")]
    [SerializeField] private int pointsToWin = 6;

    [Header("Match Reset")]
    [Tooltip("ON: every point resets both special gauges and the ball's speed back to normal.\n" +
             "OFF: they only reset when ResetMatch() is called.")]
    [SerializeField] private bool resetSpecialsEachPoint = true;

    [Header("Special Speed")]
    [Tooltip("Specials stack the ball's speed (1.5x, 2.25x, ...). It never goes faster than this many times normal.")]
    [SerializeField] private float maxSpeedMultiplier = 4f;

    /// Fires when a point ends. Argument is the winner (can be null if there's no opponent).
    public event Action<player_movemen> OnPointScored;
    /// Fires when the ball hits the table. Argument is the impact speed.
    public event Action<float> OnBounce;
    public event Action<player_movemen> OnHit;

    public BallState State { get; private set; } = BallState.Held;
    public player_movemen Server { get; private set; }
    public player_movemen LastHitter { get; private set; }
    /// True once a player has reached Points To Win.
    public bool RoundOver { get; private set; }

    /// 
    /// While true the ball is hidden and frozen and nobody can serve (used while the tutorial runs).
    /// 
    public bool WaitingToStart
    {
        get => waitingToStart;
        set
        {
            waitingToStart = value;
            SetVisible(!value);
        }
    }
    private bool waitingToStart;

    /// 
    /// Practice balls (tutorial copies) never score or serve. When their rally is over they fire
    /// OnPracticeEnded instead, and whoever spawned them decides what happens next.
    /// 
    public bool PracticeMode { get; private set; }
    public event Action<ball_controller> OnPracticeEnded;

    /// How fast the ball moves compared to normal. A special raises this and it never drops back down until a reset.
    public float SpeedMultiplier => speedFloor;
    /// True once a special has sped the ball up. Hits and bounces then cause slow-mo impact frames.
    public bool IsPowered => speedFloor > 1.0001f;
    private float speedFloor = 1f;
    private float shotSpeed = 1f;     // this shot's own speed (barely = slow, normal = fast)
    private float wallTimer;          // how long the ball has been hugging a side edge
    private float spinAcceleration;   // current sideways pull toward the middle (0 = no spin)
    private player_movemen netFaulter; // who hit it into the net (ball is rolling back to them)
    private int slowHitStreak;         // slow (barely) hits in a row this rally, both players
    private bool dribbling;            // a too-weak shot on its way into the net (the net always catches it)

    /// True while a net ball is rolling back toward the player who hit it, waiting to be rescued.
    public bool InNetRebound => netFaulter != null && State == BallState.InPlay;

    /// True while wall spin is curving the ball back toward the middle.
    public bool IsSpinning => !Mathf.Approximately(spinAcceleration, 0f);

    /// The ball's contact point on the table plane (same as the shadow).
    public Vector2 GroundPosition => transform.position;
    /// Where the ball sprite's center appears on screen (lifted up from the shadow by its height).
    public Vector2 VisualPosition => ToVisual(GroundPosition, height);
    /// Height above the table. Negative = fallen below table level.
    public float Height => height;
    public Vector2 GroundVelocity => groundVelocity;
    public float NetX => TableBounds.center.x;
    public SpriteRenderer VisualRenderer => ballVisual != null ? ballVisual.GetComponent<SpriteRenderer>() : null;
    public SpriteRenderer ShadowRenderer => shadowRenderer;

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

    private AudioSource sfxSource;

    private void Awake()
    {
        SetUpVisual();
        SetUpShadow();

        // 2D sound source for the bounce sound (reuses one if the ball already has it, e.g. practice copies).
        if (!TryGetComponent(out sfxSource)) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
    }

    private void Start()
    {
        // The tutorial (if it's switched on in the menu) runs before the first serve.
        if (!PracticeMode && game_settings.TutorialEnabled)
            tutorial_manager.Instance.Begin(this);
    }

    public void RegisterPlayer(player_movemen player)
    {
        if (!players.Contains(player)) players.Add(player);
    }

    // ---------- Practice balls (tutorial) ----------

    /// Makes a practice copy of this ball (for the tutorial). It's visible and starts with no rally.
    public ball_controller SpawnPracticeCopy()
    {
        ball_controller copy = Instantiate(this, transform.parent);
        copy.name = name + " (practice)";
        copy.PracticeMode = true;
        copy.waitingToStart = false;
        copy.SetVisible(true);
        return copy;
    }

    /// 
    /// Practice only: throws the ball from <paramref name="from"/> (at <paramref name="fromHeight"/>) so it
    /// lands on <paramref name="target"/>, as if an invisible opponent had hit it.
    /// 
    public void LaunchPractice(Vector2 from, float fromHeight, Vector2 target, float arcHeight, float speedScale)
    {
        transform.position = new Vector3(from.x, from.y, transform.position.z);
        height = fromHeight;
        Hit(null, target, arcHeight, speedScale);
    }

    private void SetVisible(bool visible)
    {
        foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (ballVisual != null && r.transform == transform && ballVisual != transform) continue; // original sprite moved to Visual
            r.enabled = visible;
        }
        if (visible) UpdateVisuals();
    }

    private void Update()
    {
        if (waitingToStart) return; // hidden until the tutorial is done

        if (State == BallState.Held)
        {
            if (Server == null) Server = firstServer != null ? firstServer : (players.Count > 0 ? players[0] : null);
            FollowServer();
        }
        else
        {
            // Running the ball's own clock faster makes every arc play out faster without changing its shape.
            Simulate(Time.deltaTime * speedFloor * shotSpeed);

            if (State == BallState.Dead)
            {
                resetTimer -= Time.deltaTime;
                if (resetTimer <= 0f && !RoundOver && !PracticeMode) // no more serves once someone has won
                {
                    if (resetSpecialsEachPoint) ResetSpecials();
                    PrepareServe(Server);
                }
            }
        }

        UpdateVisuals();
    }

    // ---------- Hitting ----------

    /// 
    /// Pops the ball up into an arc that peaks at <paramref name="arcHeight"/> above the table
    /// and comes down onto <paramref name="target"/> (a point on the table).
    /// The arc is raised automatically if it wouldn't clear the net.
    /// 
    /// <param name="speedScale">How fast this particular shot travels (1 = normal). The arc keeps its shape.</param>
    /// <param name="slowHit">A weak (barely) hit. Too many in a row and the next one can't clear the net.</param>
    public void Hit(player_movemen hitter, Vector2 target, float arcHeight, float speedScale = 1f, bool slowHit = false)
    {
        LastHitter = hitter;
        receiverBounces = 0;
        dribbling = false;
        netFaulter = null; // rescued (or a fresh shot)
        State = BallState.InPlay;
        shotSpeed = Mathf.Max(0.1f, speedScale);

        Vector2 from = GroundPosition;

        // Slow-hit streak counts both players' shots in this rally; any normal hit resets it.
        slowHitStreak = slowHit ? slowHitStreak + 1 : 0;
        bool tooManySlowHits = maxSlowHitsInARow > 0 && slowHitStreak > maxSlowHitsInARow;
        bool unluckySlowHit = UnityEngine.Random.value < slowHitRollChance;
        if (slowHit && (tooManySlowHits || unluckySlowHit))
        {
            slowHitStreak = 0;
            DribbleIntoNet(hitter, from, target);
            dribbling = true; // guaranteed to be caught by the net, whatever height it gets there at
            OnHit?.Invoke(hitter);
            return;
        }

        target = AutocorrectTarget(target, hitter);
        if (keepShotsOnTable) target = KeepPathOnTable(from, target, hitter);
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

    /// 
    /// After it lands, a ball keeps going in the same straight line. Angle the shot so that line stays on the
    /// table (Path Side Margin in from the sides) all the way to the receiver's end, so the receiver can always
    /// reach it and it never drifts off a side first. (A sharp diagonal special could do exactly that.)
    /// 
    private Vector2 KeepPathOnTable(Vector2 from, Vector2 target, player_movemen hitter)
    {
        Bounds b = TableBounds;
        int toward = hitter != null ? hitter.Facing : (target.x >= NetX ? 1 : -1);
        float endX = toward > 0 ? b.max.x : b.min.x;

        float dx = target.x - from.x;
        if (Mathf.Abs(dx) < 0.01f) return target;
        float t = (endX - from.x) / dx; // where the receiver's end is along the line (landing spot = 1)
        if (t <= 1f) return target;

        float lo = b.min.y + pathSideMargin, hi = b.max.y - pathSideMargin;
        if (lo > hi) lo = hi = b.center.y;

        float yAtEnd = from.y + (target.y - from.y) * t;
        if (yAtEnd >= lo && yAtEnd <= hi) return target;

        // Pull the landing spot in so the line reaches the end inside the table.
        yAtEnd = Mathf.Clamp(yAtEnd, lo, hi);
        target.y = from.y + (yAtEnd - from.y) / t;
        target.y = Mathf.Clamp(target.y, b.min.y + 0.2f, b.max.y - 0.2f); // still lands on the table
        return target;
    }

    /// 
    /// One slow hit too many: a soft lob that comes down right on the net, so it bounces back to the hitter as a
    /// rolling ball they have to roll-save (or lose the point).
    /// 
    private void DribbleIntoNet(player_movemen hitter, Vector2 from, Vector2 aimedTarget)
    {
        Bounds b = TableBounds;
        int toward = hitter != null ? hitter.Facing : (aimedTarget.x >= NetX ? 1 : -1);

        // Aim a little past the net line (inside the table's depth), so it reaches the net while still dropping.
        // The net always catches a dribble (see HitNet), so it can't sneak over.
        var target = new Vector2(NetX + toward * 0.6f, Mathf.Clamp(aimedTarget.y, b.min.y + 0.3f, b.max.y - 0.3f));
        float apex = Mathf.Max(dribbleArcHeight, height + 0.05f);

        float upSpeed = Mathf.Sqrt(2f * gravity * Mathf.Max(0f, apex - height));
        float flightTime = upSpeed / gravity + Mathf.Sqrt(2f * apex / gravity);
        verticalVelocity = upSpeed;
        groundVelocity = (target - from) / flightTime;
    }

    /// 
    /// Stacks a special's speed boost onto the ball: each call multiplies its speed
    /// (1.5x, then 2.25x, then 3.375x...), capped at Max Speed Multiplier.
    /// This becomes the ball's lowest speed until the match (or point) resets.
    /// 
    public void StackSpeedBoost(float multiplier)
    {
        speedFloor = Mathf.Min(speedFloor * Mathf.Max(1f, multiplier), Mathf.Max(1f, maxSpeedMultiplier));
    }

    /// 
    /// Hidden assist: if a shot would land too close to an edge of the opponent's half
    /// (sides, far end, or right by the net), nudge the landing spot back toward the middle.
    /// 
    private Vector2 AutocorrectTarget(Vector2 target, player_movemen hitter)
    {
        if (autocorrectStrength <= 0f) return target;

        int toward = hitter != null ? hitter.Facing : (target.x >= NetX ? 1 : -1);
        Rect zone = SafeLandingZone(toward);

        Vector2 safe = new Vector2(Mathf.Clamp(target.x, zone.xMin, zone.xMax), Mathf.Clamp(target.y, zone.yMin, zone.yMax));
        return Vector2.Lerp(target, safe, autocorrectStrength);
    }

    /// 
    /// Where shots are allowed to land on the half in direction <paramref name="toward"/> (+1 = right half):
    /// a little in from the net, the far end, and both sides.
    /// 
    public Rect SafeLandingZone(int toward)
    {
        Bounds b = TableBounds;
        float nearX = NetX + toward * netMargin;
        float farX = (toward > 0 ? b.max.x : b.min.x) - toward * endEdgeMargin;
        float minX = Mathf.Min(nearX, farX), maxX = Mathf.Max(nearX, farX);
        float minY = b.min.y + sideEdgeMargin, maxY = b.max.y - sideEdgeMargin;
        if (minY > maxY) minY = maxY = b.center.y;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    /// 
    /// A random spot inside the safe landing zone on the half in direction <paramref name="toward"/>.
    /// <paramref name="upDownHalf"/>: +1 = only the top half of the table, -1 = only the bottom half, 0 = anywhere.
    /// 
    public Vector2 RandomLandingSpot(int toward, int upDownHalf = 0)
    {
        Rect zone = SafeLandingZone(toward);
        float yMin = zone.yMin, yMax = zone.yMax;
        if (upDownHalf > 0) yMin = zone.center.y;
        else if (upDownHalf < 0) yMax = zone.center.y;
        return new Vector2(UnityEngine.Random.Range(zone.xMin, zone.xMax), UnityEngine.Random.Range(yMin, yMax));
    }

    /// 
    /// A landing spot <paramref name="depth"/> of the way from the net (0) to the far end (1) of the safe zone.
    /// Values are capped so shots always land a little in from the net and the table's edge.
    /// 
    public float LandingXAtDepth(int toward, float depth)
    {
        Rect zone = SafeLandingZone(toward);
        float near = toward > 0 ? zone.xMin : zone.xMax;
        float far = toward > 0 ? zone.xMax : zone.xMin;
        return Mathf.Lerp(near, far, Mathf.Clamp01(depth));
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

        if (State == BallState.InPlay && netFaulter == null) UpdateWallSpin(dt); // no spin on a rolling net ball
        groundVelocity.y += spinAcceleration * dt;
        if (State == BallState.InPlay) SlideAwayFromWalls(prevPos, prevHeight);

        Vector2 newPos = prevPos + groundVelocity * dt;
        verticalVelocity -= gravity * dt;
        height += verticalVelocity * dt;
        transform.position = new Vector3(newPos.x, newPos.y, transform.position.z);

        if (State == BallState.InPlay && HitNet(prevPos, newPos))
            return;

        // Only hit the table when coming down onto it from above (not when sliding in from under it).
        // Check where it ACTUALLY touched down during this step, not just where it ended up: a fast ball landing
        // near the far end can be past the edge by the end of the frame, and that bounce must still count.
        if (prevHeight >= 0f && height <= 0f && verticalVelocity < 0f)
        {
            float k = prevHeight - height > 0.0001f ? prevHeight / (prevHeight - height) : 1f;
            Vector2 touchdown = Vector2.Lerp(prevPos, newPos, k);
            if (IsOverTable(touchdown))
            {
                BounceOnTable(touchdown);
                return;
            }
        }

        if (height <= -floorDepth)
        {
            HitFloor();
        }
    }

    /// 
    /// If the rally keeps hugging one side of the table, slowly add spin that curves the ball back toward the middle.
    /// 
    private void UpdateWallSpin(float dt)
    {
        Bounds b = TableBounds;
        float y = GroundPosition.y;
        bool atWall = y <= b.min.y + wallZone || y >= b.max.y - wallZone;

        if (atWall) wallTimer += dt;
        else wallTimer = 0f;

        float target = 0f;
        if (wallTimer > wallTimeBeforeSpin)
        {
            float ramp = spinRampTime > 0f ? Mathf.Clamp01((wallTimer - wallTimeBeforeSpin) / spinRampTime) : 1f;
            target = Mathf.Sign(b.center.y - y) * spinStrength * ramp;
        }

        // Ease in and out so it never feels like a sudden shove.
        spinAcceleration = Mathf.MoveTowards(spinAcceleration, target, spinStrength * dt * 2f);
    }

    /// 
    /// A ball sliding / rolling along the table that touches (or hugs) a side edge is pushed back toward the
    /// middle, so it can never slide off a side where nobody can reach it.
    /// 
    private void SlideAwayFromWalls(Vector2 pos, float h)
    {
        if (h > 0.25f) return; // in the air, not sliding

        Bounds b = TableBounds;
        if (pos.x < b.min.x || pos.x > b.max.x) return;                  // past an end: it's falling off, fine
        if (pos.y < b.min.y - 0.01f || pos.y > b.max.y + 0.01f) return;  // not on the table surface

        if (pos.y <= b.min.y + wallSlideMargin && groundVelocity.y < slideAwaySpeed)
            groundVelocity.y = Mathf.Max(-groundVelocity.y, slideAwaySpeed);   // bottom edge: slide up
        else if (pos.y >= b.max.y - wallSlideMargin && groundVelocity.y > -slideAwaySpeed)
            groundVelocity.y = Mathf.Min(-groundVelocity.y, -slideAwaySpeed);  // top edge: slide down
    }

    private bool HitNet(Vector2 prevPos, Vector2 newPos)
    {
        float netX = NetX;
        bool crossed = Mathf.Sign(prevPos.x - netX) != Mathf.Sign(newPos.x - netX);
        if (!crossed || !IsWithinTableDepth(newPos.y)) return false;
        if (height >= netHeight && !dribbling) return false; // cleared it (a too-weak dribble always gets caught)

        // Knock it back onto the hitter's side and let it drop. It goes STRAIGHT back (no sideways drift and
        // no wall spin), so it always rolls off the hitter's end where it can be saved, never off a side.
        transform.position = new Vector3(prevPos.x, prevPos.y, transform.position.z);
        groundVelocity = new Vector2(-groundVelocity.x * 0.15f, 0f);
        verticalVelocity = Mathf.Min(verticalVelocity, 0f);
        spinAcceleration = 0f;
        wallTimer = 0f;
        dribbling = false;

        // It's over the table here. If it reached the net already below the surface, put it back ON the table,
        // otherwise it would sink straight through to the floor and wrongly end the point.
        height = Mathf.Max(height, 0f);

        // Still a live ball: it rolls back toward the hitter, who can hit it again to keep the rally going.
        // Clearing LastHitter lets them swing at it again (and skips the "bounced on your own side" fault).
        // If it reaches the floor without being rescued, the other player gets the point (see HitFloor).
        netFaulter = LastHitter;
        LastHitter = null;
        receiverBounces = 0;
        return true;
    }

    private void BounceOnTable(Vector2 pos)
    {
        float impactSpeed = -verticalVelocity;

        height = 0f;
        verticalVelocity = impactSpeed * bounciness;
        if (verticalVelocity < 0.5f) verticalVelocity = 0f; // settle instead of jittering
        else squashTimer = squashDuration;                  // visible smack on real impacts

        if (tableBounceSound != null && impactSpeed >= minBounceSpeedForSound && !waitingToStart)
            sfxSource.PlayOneShot(tableBounceSound, tableBounceVolume);

        OnBounce?.Invoke(impactSpeed);

        if (State != BallState.InPlay || LastHitter == null) return;

        // Bounces are only remembered here. The point isn't decided until the ball actually leaves play
        // (reaches the floor, see HitFloor), so the score never changes while the ball is still moving
        // and anyone can still hit it until then.
        // (A bounce on the hitter's OWN side isn't a fault: shots always aim at the other half, so that only
        // happens from frame-step error right before the net. If it then can't get over, the net catches it.)
        int side = pos.x < NetX ? -1 : 1;
        if (side != LastHitter.Side) receiverBounces++;
    }

    private void HitFloor()
    {
        height = -floorDepth;
        verticalVelocity = 0f;
        groundVelocity = Vector2.zero;

        if (State != BallState.InPlay) return;

        // A net ball that nobody rescued: the player who hit it into the net loses the point.
        if (netFaulter != null)
        {
            EndPoint(Opponent(netFaulter));
            return;
        }

        // Left the table without touching the opponent's side = hitter's fault.
        // Bounced on the opponent's side and nobody returned it = the opponent missed it.
        bool hitterFault = receiverBounces == 0;
        EndPoint(hitterFault ? Opponent(LastHitter) : LastHitter);
    }

    // ---------- Points / serving ----------

    private void EndPoint(player_movemen winner)
    {
        State = BallState.Dead;
        resetTimer = pointResetDelay;
        spinAcceleration = 0f;
        wallTimer = 0f;
        netFaulter = null;
        dribbling = false;
        slowHitStreak = 0;

        if (PracticeMode)
        {
            OnPracticeEnded?.Invoke(this); // practice balls never score
            return;
        }

        if (winner != null)
        {
            winner.AddPoint();
            Server = winner; // winner serves next
        }

        OnPointScored?.Invoke(winner);

        if (winner != null && pointsToWin > 0 && winner.Points >= pointsToWin)
        {
            RoundOver = true;
            round_end.Instance.Play(winner, Opponent(winner));
        }
    }

    /// Puts the ball on <paramref name="server"/>'s paddle, waiting for them to hit.
    public void PrepareServe(player_movemen server)
    {
        Server = server;
        LastHitter = null;
        receiverBounces = 0;
        dribbling = false;
        groundVelocity = Vector2.zero;
        verticalVelocity = 0f;
        netFaulter = null;
        slowHitStreak = 0;
        shotSpeed = 1f;
        wallTimer = 0f;
        spinAcceleration = 0f;
        State = BallState.Held;
        FollowServer();
    }

    /// 
    /// Restarts the match: scores to 0, both special gauges emptied, ball back to normal speed,
    /// time back to normal, and a fresh serve. Call this from your match-over / restart code.
    /// 
    public void ResetMatch()
    {
        RoundOver = false;
        foreach (player_movemen p in players) p.ResetPoints();
        ResetSpecials();
        match_effects.Instance.ClearTableMarks(); // marks last between points, but not into a new match
        PrepareServe(firstServer != null ? firstServer : (players.Count > 0 ? players[0] : null));
    }

    /// Empties every player's special gauge, cancels any special in progress, and returns the ball to normal speed.
    public void ResetSpecials()
    {
        foreach (player_movemen p in players)
        {
            if (p.TryGetComponent(out special_ability ability)) ability.Cancel();
            p.ResetSpecial();
        }

        speedFloor = 1f;
        match_effects.Instance.ResetTime();
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

    /// 
    /// Simulates ahead (including table bounces) to find where the ball will LOOK like it is
    /// when its shadow reaches world X = <paramref name="x"/>. Returns false if it isn't heading there.
    /// 
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
            v.y += spinAcceleration * step; // include current spin
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
                time = t / (speedFloor * shotSpeed); // real seconds, accounting for special boosts and shot speed
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

    /// If no visual is assigned, moves this object's sprite onto a "Visual" child so it can be lifted.
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
        // From the sprite itself (not renderer bounds), so it's right even if the renderer starts hidden.
        ballRadius = visualRenderer != null && visualRenderer.sprite != null
            ? visualRenderer.sprite.bounds.extents.y * Mathf.Abs(ballVisual.lossyScale.y)
            : 0f;
    }

    /// If no shadow is assigned, creates a pixel-art ellipse shadow sized to match the ball.
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
