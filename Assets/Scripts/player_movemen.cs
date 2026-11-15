using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// Ping pong player: moves up/down along their end of the table and swings a paddle at the ball.
/// Works for either side of the table - put one on the left as Player 1 and one on the right as Player 2.
/// Hitting the ball near the very edge of the paddle's reach (a "barely hit") fills the special gauge.
public class player_movemen : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("1 or 2. Picks the default controls. Which side of the table you're on is worked out from your position.")]
    [Range(1, 2)]
    [SerializeField] private int playerNumber = 1;
    [SerializeField] private ball_controller ball;

    [Header("Controls (None = default: P1 W/S/Space, P2 Up/Down/Enter)")]
    [SerializeField] private Key upKey = Key.None;
    [SerializeField] private Key downKey = Key.None;
    [SerializeField] private Key hitKey = Key.None;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [Tooltip("Keep the player lined up with the table's depth (uses the ball's table bounds). If off, uses Min/Max Y.")]
    [SerializeField] private bool clampToTable = true;
    [Tooltip("How far past the table's near/far edge the player can still move.")]
    [SerializeField] private float tableEdgePadding = 0.5f;
    [SerializeField] private float minY = -7f;
    [SerializeField] private float maxY = -1f;

    [Header("Paddle")]
    [Tooltip("Optional. Where the paddle is. If empty, Paddle Offset is used (X is flipped automatically to face the table).")]
    [SerializeField] private Transform paddlePoint;
    [SerializeField] private Vector2 paddleOffset = new Vector2(0.8f, 0f);
    [Tooltip("How far the paddle can reach the ball, measured to where the ball LOOKS like it is on screen (not its shadow).")]
    [SerializeField] private float hitRadius = 1.2f;
    [Tooltip("How long a swing stays 'live' after pressing hit.")]
    [SerializeField] private float swingDuration = 0.25f;
    [SerializeField] private float swingCooldown = 0.15f;

    [Header("Shot")]
    [Tooltip("How high above the table the shot arcs before coming down onto the opponent's side.")]
    [SerializeField] private float shotArcHeight = 2.5f;
    [Tooltip("Arc height for serves.")]
    [SerializeField] private float serveArcHeight = 1.5f;
    [Tooltip("Where shots land on the opponent's half: 0 = just past the net, 1 = just before their end of the table. " +
             "(The ball's Net Margin / End Edge Margin set how close 'just' is.)")]
    [Range(0f, 1f)]
    [FormerlySerializedAs("shotDepth")]
    [SerializeField] private float serveDepth = 0.65f;
    [Tooltip("Normal hits land deep...")]
    [Range(0f, 1f)]
    [SerializeField] private float normalHitDepth = 0.9f;
    [Tooltip("...and travel faster than usual (1 = normal speed).")]
    [SerializeField] private float normalHitSpeed = 1.15f;
    [Tooltip("Barely hits land short, close to the net...")]
    [Range(0f, 1f)]
    [SerializeField] private float barelyHitDepth = 0.15f;
    [Tooltip("...and travel slower than usual (1 = normal speed).")]
    [SerializeField] private float barelyHitSpeed = 0.75f;
    [Tooltip("How far up/down the shot is aimed when holding up or down while hitting.")]
    [SerializeField] private float aimAmount = 1.5f;

    [Header("Special Gauge")]
    [SerializeField] private float maxSpecial = 100f;
    [Tooltip("A hit counts as 'barely' when the ball is at least this fraction of Hit Radius away from the paddle.")]
    [Range(0f, 1f)]
    [SerializeField] private float barelyHitThreshold = 0.75f;
    [Tooltip("Gauge gained from a normal (clean) hit.")]
    [FormerlySerializedAs("barelyHitGain")]
    [SerializeField] private float normalHitGain = 20f;
    [Tooltip("Barely hits gain Normal Hit Gain times this (0.25 = a quarter as much).")]
    [Range(0f, 1f)]
    [SerializeField] private float barelyHitGainMultiplier = 0.25f;

    [Header("Super Shot Aim (moving up = top diagonal, still = middle, moving down = bottom diagonal)")]
    [Tooltip("How deep supers aim: 0 = just past the net, 1 = the far end (inside the safe zone).")]
    [Range(0f, 1f)]
    [SerializeField] private float superDepth = 0.9f;
    [Tooltip("Random wobble in how deep it lands (0-1 of the half). Small = consistent.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float superDepthVariation = 0.08f;
    [Tooltip("Random wobble up/down around the aimed corner or middle (world units). Small = consistent.")]
    [SerializeField] private float superSideVariation = 0.35f;
    [Tooltip("Pulls the diagonal aim from the corner toward the middle of the table (0 = right at the corner, " +
             "1 = dead center).")]
    [Range(0f, 1f)]
    [SerializeField] private float superCornerPull = 0.35f;
    [Tooltip("Steer each super so the other player can always reach it in time from where they're standing.")]
    [SerializeField] private bool keepSupersReturnable = true;
    [Tooltip("Safety margin for that: the fraction of the receiver's full reach they're expected to use (lower = easier).")]
    [Range(0.3f, 1f)]
    [SerializeField] private float superReturnSafety = 0.8f;

    [Header("Hit Sounds (leave empty for no sound)")]
    [Tooltip("Played on every hit that isn't a special (normal hits, serves, and barely hits if Weak Hit Sound is empty).")]
    [SerializeField] private AudioClip hitSound;
    [Tooltip("Played on weak (barely) hits, except the one that fills the special meter (that one plays Hit Sound). " +
             "Empty = weak hits play Hit Sound like everything else.")]
    [SerializeField] private AudioClip weakHitSound;
    [Tooltip("Played the moment a special is hit.")]
    [SerializeField] private AudioClip specialHitSound;
    [Range(0f, 1f)]
    [SerializeField] private float hitSoundVolume = 1f;

    [Header("Animation (optional)")]
    [SerializeField] private animation_controller animator;
    [SerializeField] private string idleAnimation = "Idle";
    [SerializeField] private string swingAnimation = "Swing";
    [Tooltip("Plays instead of the swing on a barely hit. Turn Loop off so it returns to idle.")]
    [SerializeField] private string barelyHitAnimation = "BarelyHit";
    [Tooltip("Plays when the special gauge fills up. Turn Loop off so it returns to idle.")]
    [SerializeField] private string specialReadyAnimation = "SpecialReady";
    [Tooltip("Plays when the special shot is used.")]
    [SerializeField] private string specialShotAnimation = "SpecialShot";
    [Tooltip("Flip the sprite on X when on the right side of the table.")]
    [SerializeField] private bool flipSpriteOnRightSide = true;

    /// Fires whenever the special gauge changes. Value is 0-1.
    public event Action<float> OnSpecialChanged;
    /// Fires on a barely hit (for everyone, bots included).
    public event Action OnBarelyHit;
    /// Fires when gauge is gained: (from 0-1, to 0-1, was it a barely hit).
    public event Action<float, float, bool> OnSpecialGained;
    /// Fires the moment the special gauge fills up.
    public event Action OnSpecialReady;
    /// Fires when a full gauge is spent on a special shot.
    public event Action OnSpecialShot;
    public event Action OnSwing;

    public int PlayerNumber => playerNumber;
    /// -1 = left side of the table, +1 = right side.
    public int Side { get; private set; }
    /// Direction toward the opponent on X (+1 or -1).
    public int Facing => -Side;
    public float Special { get; private set; }
    public float SpecialNormalized => maxSpecial > 0f ? Special / maxSpecial : 0f;
    public bool SpecialIsFull => Special >= maxSpecial;
    /// Bots don't have a special gauge.
    public bool HasSpecialGauge => !BotActive;
    public int Points { get; private set; }
    public bool IsSwinging => swingTimer > 0f;

    public Vector2 PaddlePoint =>
        paddlePoint != null
            ? (Vector2)paddlePoint.position
            : (Vector2)transform.position + new Vector2(paddleOffset.x * Facing, paddleOffset.y);

    /// Distance from the paddle where normal hits end and barely hits begin.
    public float BarelyRadius => hitRadius * barelyHitThreshold;

    public ball_controller Ball => ball;
    /// When true the player ignores input (used when the round ends).
    public bool ControlsLocked { get; set; }
    public float SwingDuration => swingDuration;
    public float MoveSpeed => moveSpeed;
    public float HitRadius => hitRadius;

    /// True when a bot is playing for this player.
    public bool IsBot => BotActive;
    public Key UpKey => upKey;
    public Key DownKey => downKey;
    public Key HitKey => hitKey;
    /// The up/down input from the latest frame (-1 to 1).
    public float LastVertical { get; private set; }

    /// 
    /// Tutorial: swings go at this practice ball instead of the real one. Set to null to go back to the real ball.
    /// 
    public void SetPracticeBall(ball_controller practice) => practiceBall = practice;
    public ball_controller PracticeBall => practiceBall;
    private ball_controller practiceBall;

    /// The ball swings are aimed at right now (the tutorial's practice ball, or the real one).
    private ball_controller HitBall => practiceBall != null ? practiceBall : ball;

    private float swingTimer;
    private float cooldownTimer;
    private bool hitThisSwing;
    private float closestDist = float.MaxValue;
    private player_bot bot;
    private special_ability special;
    private bool BotActive => bot != null && bot.isActiveAndEnabled;

    private void Awake()
    {
        // Player 2 is a bot or a human depending on the menu's "P2 Bot" toggle.
        if (playerNumber == 2)
        {
            bool wantBot = game_settings.Player2IsBot;
            if (!TryGetComponent(out player_bot existing) && wantBot) existing = gameObject.AddComponent<player_bot>();
            if (existing != null) existing.enabled = wantBot;
        }

        TryGetComponent(out bot);
        TryGetComponent(out special);

        // 2D sound source for hit sounds (sounds keep playing through a special's time stop).
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        if (upKey == Key.None) upKey = playerNumber == 1 ? Key.W : Key.UpArrow;
        if (downKey == Key.None) downKey = playerNumber == 1 ? Key.S : Key.DownArrow;
        if (hitKey == Key.None) hitKey = playerNumber == 1 ? Key.Space : Key.Enter;

        if (animator == null) animator = GetComponent<animation_controller>();
        if (animator != null) animator.OnAnimationFinished += HandleAnimationFinished;
    }

    private void OnDestroy()
    {
        if (animator != null) animator.OnAnimationFinished -= HandleAnimationFinished;
    }

    // One-shot animations (barely hit, special ready, etc.) go back to idle when they end.
    private void HandleAnimationFinished(string finished)
    {
        if (finished != idleAnimation) PlayAnimation(idleAnimation);
    }

    private void Start()
    {
        float centerX = ball != null ? ball.NetX : 0f;
        Side = transform.position.x < centerX ? -1 : 1;

        if (ball != null)
            ball.RegisterPlayer(this);
        else
            Debug.LogWarning($"[player_movemen] No ball assigned on '{name}'.", this);

        if (flipSpriteOnRightSide && TryGetComponent(out SpriteRenderer sr))
            sr.flipX = Side > 0;
    }

    private void Update()
    {
        // Time is stopped for a special (or the round is over): no moving or swinging.
        if (Time.timeScale == 0f || ControlsLocked) return;

        float vertical = ReadVertical();
        LastVertical = vertical;
        Move(vertical);

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        if (!IsSwinging && cooldownTimer <= 0f && ReadHitPressed())
            StartSwing();

        if (IsSwinging)
        {
            if (!hitThisSwing) TryHit(ReadAim());

            swingTimer -= Time.deltaTime;
            if (swingTimer <= 0f)
            {
                cooldownTimer = swingCooldown;
                // Don't cut off a barely-hit / special animation that started during this swing.
                if (animator == null || animator.CurrentAnimationName == swingAnimation)
                    PlayAnimation(idleAnimation);
            }
        }
    }

    // ---------- Movement ----------

    private void Move(float vertical)
    {
        Vector3 pos = transform.position;
        pos.y += vertical * moveSpeed * Time.deltaTime;

        float lo = minY, hi = maxY;
        if (clampToTable && ball != null)
        {
            Bounds table = ball.TableBounds;
            float offsetY = paddlePoint != null ? paddlePoint.position.y - transform.position.y : paddleOffset.y;
            lo = table.min.y - tableEdgePadding - offsetY;
            hi = table.max.y + tableEdgePadding - offsetY;
        }

        pos.y = Mathf.Clamp(pos.y, lo, hi);
        transform.position = pos;
    }

    // ---------- Hitting ----------

    private void StartSwing()
    {
        swingTimer = swingDuration;
        hitThisSwing = false;
        closestDist = float.MaxValue;
        PlayAnimation(swingAnimation, restart: true);
        OnSwing?.Invoke();

        // Serving: the ball is waiting on our paddle, so just send it.
        // (Not while the game is waiting on the tutorial.)
        if (HitBall != null && !HitBall.WaitingToStart && HitBall.State == ball_controller.BallState.Held && HitBall.Server == this)
        {
            HitBall.Hit(this, GetShotTarget(ReadAim(), serveDepth), serveArcHeight);
            PlayHitSound(hitSound);
            hitThisSwing = true;
        }
    }

    private void TryHit(float aim)
    {
        if (HitBall == null || HitBall.State != ball_controller.BallState.InPlay) return;
        if (HitBall.LastHitter == this) return; // already hit it, wait for the return

        // Measured to the ball sprite on screen, so a ball high in the air is out of reach until it comes down.
        float dist = Vector2.Distance(PaddlePoint, HitBall.VisualPosition);
        if (dist > hitRadius)
        {
            closestDist = float.MaxValue;
            return;
        }

        // Wait until the ball is as close as it's going to get (or the swing is about to end),
        // so swinging early isn't punished and "barely" really means the ball was almost out of reach.
        bool movingAway = dist > closestDist;
        bool lastChance = swingTimer <= Time.deltaTime;
        closestDist = Mathf.Min(closestDist, dist);
        if (!movingAway && !lastChance) return;

        hitThisSwing = true;

        // Gauge was full going into this hit: spend it on the special shot instead (never on practice balls).
        if (HasSpecialGauge && SpecialIsFull && practiceBall == null)
        {
            UseSpecialShot(aim);
            return;
        }

        // Barely hit: at its closest, the ball was right at the edge of the paddle's reach
        // (paddle lined up badly, or swung too late). Barely = slow and short, normal = fast and deep.
        bool barely = closestDist / hitRadius >= barelyHitThreshold;
        HitBall.Hit(this,
                 GetShotTarget(aim, barely ? barelyHitDepth : normalHitDepth),
                 shotArcHeight,
                 barely ? barelyHitSpeed : normalHitSpeed,
                 slowHit: barely); // too many slow hits in a row and this one dribbles into the net

        bool wasFull = SpecialIsFull;
        if (barely)
        {
            PlayAnimation(barelyHitAnimation, restart: true);
            OnBarelyHit?.Invoke();
            AddSpecial(normalHitGain * barelyHitGainMultiplier, barely: true);
        }
        else
        {
            AddSpecial(normalHitGain, barely: false);
        }

        // Weak hits get their own sound, unless this was the hit that filled the special meter.
        bool filledMeter = !wasFull && SpecialIsFull;
        bool useWeakSound = barely && !filledMeter && weakHitSound != null;
        PlayHitSound(useWeakSound ? weakHitSound : hitSound);
    }

    private void UseSpecialShot(float aim)
    {
        Special = 0f;
        OnSpecialChanged?.Invoke(0f);
        PlayAnimation(specialShotAnimation, restart: true);
        OnSpecialShot?.Invoke();

        // The special flies to the far end of the opponent's side: the top diagonal if the player is moving up,
        // the middle if they're still, the bottom diagonal if moving down, with a little random wobble.
        // Always at normal-hit speed.
        int half = aim > 0.1f ? 1 : (aim < -0.1f ? -1 : 0);
        Vector2 superTarget = GetSuperTarget(half);
        HitBall.Hit(this, superTarget, shotArcHeight, normalHitSpeed);
        if (keepSupersReturnable) KeepSuperReturnable(superTarget);
        PlayHitSound(specialHitSound);

        // =====================================================================
        //  SPECIAL SHOT
        //  The time stop -> cutscene -> pose -> faster ball sequence lives in
        //  special_ability.cs. Add extra special-only effects here or there.
        // =====================================================================
        if (special != null)
            special.Activate();
        else
            Debug.LogWarning($"[player_movemen] '{name}' used a special but has no special_ability component.", this);
    }

    /// 
    /// Picks where the shot lands on the opponent's half. <paramref name="depth"/>: 0 = just past the net,
    /// 1 = just before their end (capped inside the safe zone). Holding up/down aims the shot sideways.
    /// The ball's autocorrect pulls it back toward the middle if this ends up near a side edge.
    /// 
    /// <summary>
    /// Where a super aims: the far end of the opponent's side, at the top corner (<paramref name="half"/> = +1,
    /// moving up), the middle (0, standing still) or the bottom corner (-1, moving down), plus a small wobble.
    /// Kept inside the safe landing zone.
    /// </summary>
    private Vector2 GetSuperTarget(int half)
    {
        Rect zone = HitBall.SafeLandingZone(Facing);

        float depth = Mathf.Clamp01(superDepth + UnityEngine.Random.Range(-superDepthVariation, superDepthVariation));
        float x = HitBall.LandingXAtDepth(Facing, depth);

        float cornerY = half > 0 ? zone.yMax : half < 0 ? zone.yMin : zone.center.y;
        float aimY = Mathf.Lerp(cornerY, zone.center.y, superCornerPull); // a little in from the corner
        float y = Mathf.Clamp(aimY + UnityEngine.Random.Range(-superSideVariation, superSideVariation), zone.yMin, zone.yMax);

        return new Vector2(x, y);
    }

    /// <summary>
    /// Makes sure the other player can actually return a super: predicts where (and when) the ball will pass
    /// their paddle, including the speed boost that kicks in after the special, and if they couldn't get there
    /// in time from where they're standing, steers the landing spot toward them and re-aims (a few tries).
    /// </summary>
    private void KeepSuperReturnable(Vector2 target)
    {
        player_movemen receiver = FindOpponent();
        if (receiver == null) return;

        float boost = special != null ? Mathf.Max(1f, special.BallSpeedMultiplier) : 1f;
        Rect zone = HitBall.SafeLandingZone(Facing);
        Vector2 from = HitBall.GroundPosition;

        for (int i = 0; i < 4; i++)
        {
            Vector2 paddle = receiver.PaddlePoint;
            if (!HitBall.PredictVisualAtX(paddle.x, out Vector2 passing, out float time)) return;

            time /= boost; // the ball speeds up after the special's cutscene
            float reach = (receiver.MoveSpeed * time + receiver.HitRadius * 0.8f) * superReturnSafety;
            float tooFar = Mathf.Abs(passing.y - paddle.y) - reach;
            if (tooFar <= 0f) return; // reachable

            // Moving the landing spot shifts where it passes the receiver by roughly k times as much.
            float dx = target.x - from.x;
            float k = Mathf.Abs(dx) > 0.01f ? Mathf.Max(1f, (paddle.x - from.x) / dx) : 1f;
            target.y = Mathf.Clamp(target.y - Mathf.Sign(passing.y - paddle.y) * tooFar / k, zone.yMin, zone.yMax);
            HitBall.Hit(this, target, shotArcHeight, normalHitSpeed);
        }
    }

    private player_movemen FindOpponent()
    {
        foreach (player_movemen p in FindObjectsByType<player_movemen>())
            if (p != this && p.Ball == ball) return p;
        return null;
    }

    private Vector2 GetShotTarget(float aim, float depth)
    {
        float x = HitBall.LandingXAtDepth(Facing, depth);
        float y = HitBall.GroundPosition.y + aim * aimAmount;
        return new Vector2(x, y);
    }

    // ---------- Special gauge ----------

    public void AddSpecial(float amount, bool barely = false)
    {
        if (!HasSpecialGauge) return;

        bool wasFull = SpecialIsFull;
        float before = Special;
        float beforeNormalized = SpecialNormalized;
        Special = Mathf.Clamp(Special + amount, 0f, maxSpecial);
        if (Mathf.Approximately(before, Special)) return;

        if (Special > before)
            OnSpecialGained?.Invoke(beforeNormalized, SpecialNormalized, barely);
        OnSpecialChanged?.Invoke(SpecialNormalized);

        if (!wasFull && SpecialIsFull)
        {
            PlayAnimation(specialReadyAnimation, restart: true);
            OnSpecialReady?.Invoke();
        }
    }

    public void AddPoint() => Points++;
    public void ResetPoints() => Points = 0;

    /// Empties the special gauge (used when the match resets).
    public void ResetSpecial()
    {
        Special = 0f;
        OnSpecialChanged?.Invoke(0f);
    }

    // ---------- Input / helpers ----------

    private bool ReadHitPressed() => BotActive ? bot.ConsumeHit() : WasPressed(hitKey);

    /// Up/down aim for shots. Humans aim with the movement keys; bots choose their own.
    private float ReadAim() => BotActive ? bot.Aim : ReadVertical();

    private float ReadVertical()
    {
        if (BotActive) return bot.Vertical;

        float v = 0f;
        if (IsHeld(upKey)) v += 1f;
        if (IsHeld(downKey)) v -= 1f;
        return v;
    }

    private static bool IsHeld(Key key)
    {
        Keyboard kb = Keyboard.current;
        return kb != null && key != Key.None && kb[key].isPressed;
    }

    private static bool WasPressed(Key key)
    {
        Keyboard kb = Keyboard.current;
        return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
    }

    private AudioSource sfxSource;

    /// Plays a hit sound if one is set (empty = silent).
    private void PlayHitSound(AudioClip clip)
    {
        if (clip != null && sfxSource != null) sfxSource.PlayOneShot(clip, hitSoundVolume);
    }

    private void PlayAnimation(string animName, bool restart = false)
    {
        if (animator != null && !string.IsNullOrEmpty(animName) && animator.HasAnimation(animName))
            animator.Play(animName, restart);
    }

    private void OnDrawGizmosSelected()
    {
        // Side isn't known until play mode, so guess from player number in the editor.
        int facing = Application.isPlaying ? Facing : (playerNumber == 1 ? 1 : -1);
        Vector3 p = paddlePoint != null
            ? paddlePoint.position
            : transform.position + new Vector3(paddleOffset.x * facing, paddleOffset.y, 0f);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(p, hitRadius);
        Gizmos.color = Color.yellow; // inside this ring = normal hit, between rings = barely hit
        Gizmos.DrawWireSphere(p, hitRadius * barelyHitThreshold);
    }
}
