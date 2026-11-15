using UnityEngine;

/// <summary>
/// Match-wide effects:
///  - Full time stop while a special's cutscene plays (used by special_ability).
///  - Plays an "impact" animation at the ball when it hits the table (after a special, or every bounce).
///
/// Optional to place in the scene (to tweak settings). If it's missing, one is created automatically.
/// </summary>
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

    /// <summary>True while a special has time completely stopped.</summary>
    public bool TimeStopped { get; private set; }

    private ball_controller subscribedBall;
    private bool warnedNoImpactPrefab;

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

    /// <summary>Back to normal time immediately (used when a point / match resets).</summary>
    public void ResetTime() => ResumeTime();

    // ---------- Impact animation ----------

    private void HookBall()
    {
        if (ball == null) ball = FindAnyObjectByType<ball_controller>();
        if (ball == subscribedBall) return;

        if (subscribedBall != null) subscribedBall.OnBounce -= HandleBounce;
        subscribedBall = ball;
        if (subscribedBall != null) subscribedBall.OnBounce += HandleBounce;
    }

    private void HandleBounce(float impactSpeed)
    {
        if (ball.State != ball_controller.BallState.InPlay) return; // ignore the little settling bounces after a point
        if (ball.IsPowered || impactOnEveryBounce)
            SpawnImpact(ball.VisualPosition + impactOffset);
    }

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
