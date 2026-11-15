using UnityEngine;

/// <summary>
/// Controls game time and camera for specials:
///  - Full time stop while a special's cutscene plays (used by special_ability).
///  - After a special has powered up the ball: as the ball is about to hit the table, time slows and the
///    camera zooms in on the ball. Once it has bounced back up off the table, the camera zooms back out
///    and time returns to normal.
///  - Plays an "impact" animation at the ball when it hits the table.
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
    [SerializeField] private Camera cam;

    [Header("Bounce Slow-Mo (after a special)")]
    [Tooltip("Time speed while slowed (0.2 = 20% speed).")]
    [Range(0.01f, 1f)]
    [SerializeField] private float slowMoScale = 0.2f;
    [Tooltip("Start slowing this many real seconds before the ball touches the table.")]
    [SerializeField] private float slowMoLeadTime = 0.15f;
    [Tooltip("Go back to normal once the ball has bounced back up to this height.")]
    [SerializeField] private float releaseHeight = 0.6f;
    [Tooltip("Safety limit: never stay slowed longer than this many real seconds.")]
    [SerializeField] private float maxSlowMoTime = 1.5f;
    [Tooltip("Real seconds to slow down / speed back up.")]
    [SerializeField] private float slowDownDuration = 0.08f;
    [SerializeField] private float speedUpDuration = 0.2f;

    [Header("Camera Zoom")]
    [Tooltip("Camera size while zoomed, as a fraction of normal (0.55 = zoomed in a lot).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float zoomAmount = 0.55f;
    [SerializeField] private float zoomInDuration = 0.12f;
    [SerializeField] private float zoomOutDuration = 0.25f;

    [Header("Impact Animation")]
    [Tooltip("Spawned at the ball when it hits the table. Should have an animation_controller with an animation " +
             "named Impact Animation (set Loop off). It's destroyed when the animation finishes.")]
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private string impactAnimation = "impact";
    [Tooltip("OFF: only after a special has powered up the ball. ON: every table bounce.")]
    [SerializeField] private bool impactOnEveryBounce = false;
    [SerializeField] private Vector2 impactOffset = Vector2.zero;
    [Tooltip("Safety: destroy the impact object after this long even if its animation never finishes.")]
    [SerializeField] private float impactMaxLifetime = 2f;

    /// <summary>True while a special has time completely stopped.</summary>
    public bool TimeStopped { get; private set; }
    /// <summary>True while the bounce slow-mo / zoom is active.</summary>
    public bool SlowMoActive { get; private set; }

    private bool bouncedDuringSlowMo;
    private float slowMoTimer;
    private float timeBlend; // 0 = normal speed, 1 = full slow-mo
    private float zoomBlend; // 0 = normal camera, 1 = fully zoomed on the ball

    private bool cameraCaptured;
    private float baseCamSize;
    private Vector3 baseCamPos;
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

    private void Start()
    {
        CaptureCamera();
        HookBall();
    }

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
        Time.timeScale = Mathf.Lerp(1f, slowMoScale, timeBlend);
    }

    /// <summary>Back to normal time and camera immediately (used when a point / match resets).</summary>
    public void ResetTime()
    {
        TimeStopped = false;
        SlowMoActive = false;
        timeBlend = 0f;
        zoomBlend = 0f;
        Time.timeScale = 1f;
        ApplyZoom();
    }

    // ---------- Bounce slow-mo + zoom ----------

    private void Update()
    {
        HookBall();
        if (TimeStopped) return; // the cutscene owns time right now

        float dt = Time.unscaledDeltaTime;

        if (!SlowMoActive)
        {
            // About to touch down on the table? Start slowing + zooming.
            if (ball != null && ball.IsPowered
                && ball.PredictTableContact(out float timeToContact, out _)
                && timeToContact <= slowMoLeadTime)
            {
                SlowMoActive = true;
                bouncedDuringSlowMo = false;
                slowMoTimer = 0f;
            }
        }
        else
        {
            // Release once the ball has bounced and climbed back up off the table.
            slowMoTimer += dt;
            bool release = ball == null
                || ball.State != ball_controller.BallState.InPlay
                || !ball.IsPowered
                || slowMoTimer >= maxSlowMoTime
                || (bouncedDuringSlowMo && ball.Height >= releaseHeight);
            if (release) SlowMoActive = false;
        }

        float target = SlowMoActive ? 1f : 0f;
        timeBlend = Mathf.MoveTowards(timeBlend, target, dt / Mathf.Max(0.001f, SlowMoActive ? slowDownDuration : speedUpDuration));
        zoomBlend = Mathf.MoveTowards(zoomBlend, target, dt / Mathf.Max(0.001f, SlowMoActive ? zoomInDuration : zoomOutDuration));

        Time.timeScale = Mathf.Lerp(1f, slowMoScale, timeBlend);
    }

    private void LateUpdate()
    {
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (!cameraCaptured) return;

        float z = Mathf.SmoothStep(0f, 1f, zoomBlend);
        cam.orthographicSize = Mathf.Lerp(baseCamSize, baseCamSize * zoomAmount, z);

        Vector3 focus = baseCamPos;
        if (ball != null)
        {
            Vector2 b = ball.VisualPosition;
            focus = new Vector3(b.x, b.y, baseCamPos.z);
        }
        cam.transform.position = Vector3.Lerp(baseCamPos, focus, z);
    }

    private void CaptureCamera()
    {
        if (cameraCaptured) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        baseCamSize = cam.orthographicSize;
        baseCamPos = cam.transform.position;
        cameraCaptured = true;
    }

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

        if (SlowMoActive) bouncedDuringSlowMo = true;
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

        GameObject fx = Instantiate(impactPrefab, new Vector3(pos.x, pos.y, impactPrefab.transform.position.z), Quaternion.identity);

        var anim = fx.GetComponentInChildren<animation_controller>();
        if (anim != null && anim.HasAnimation(impactAnimation))
        {
            anim.Play(impactAnimation, restartIfSame: true);
            anim.OnAnimationFinished += _ => { if (fx != null) Destroy(fx); };
        }

        Destroy(fx, impactMaxLifetime);
    }
}
