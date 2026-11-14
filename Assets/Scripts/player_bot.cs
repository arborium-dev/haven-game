using UnityEngine;

/// <summary>
/// Simple test bot. Add it to any player object (next to player_movemen) and that player
/// plays itself: it lines up with incoming shots, swings, and serves.
/// Disable the component to hand control back to the keyboard.
/// </summary>
[RequireComponent(typeof(player_movemen))]
public class player_bot : MonoBehaviour
{
    [Header("Reactions")]
    [Tooltip("Seconds after the opponent hits before the bot starts moving.")]
    [SerializeField] private float reactionDelay = 0.15f;
    [Tooltip("Seconds the bot waits before serving.")]
    [SerializeField] private float serveDelay = 0.8f;

    [Header("Mistakes")]
    [Tooltip("Random up/down error in where the bot lines up its paddle. " +
             "Bigger = more barely hits (special gauge) and more misses.")]
    [SerializeField] private float positionError = 0.4f;
    [Tooltip("Chance (0-1) the bot doesn't swing at all.")]
    [Range(0f, 1f)]
    [SerializeField] private float missChance = 0.05f;

    [Header("Shots")]
    [Tooltip("Chance (0-1) the bot aims a shot up or down instead of straight.")]
    [Range(0f, 1f)]
    [SerializeField] private float aimChance = 0.5f;

    [Header("Movement")]
    [Tooltip("The bot stops moving once its paddle is this close to where it wants to be.")]
    [SerializeField] private float deadZone = 0.08f;
    [Tooltip("Distance at which the bot moves at full speed. Smaller = snappier.")]
    [SerializeField] private float fullSpeedDistance = 0.4f;

    /// <summary>Up/down input (-1 to 1), read by player_movemen.</summary>
    public float Vertical { get; private set; }
    /// <summary>Shot aim (-1, 0 or 1), read by player_movemen.</summary>
    public float Aim { get; private set; }

    private player_movemen player;
    private bool hitQueued;

    // What the bot is currently reacting to. When either changes, it's a new shot.
    private player_movemen trackedHitter;
    private ball_controller.BallState trackedState;

    private float reactionTimer;
    private float serveTimer;
    private float errorY;
    private bool willMiss;
    private bool swung;

    /// <summary>Returns true once per bot "button press". Called by player_movemen.</summary>
    public bool ConsumeHit()
    {
        bool pressed = hitQueued;
        hitQueued = false;
        return pressed;
    }

    private void Awake()
    {
        player = GetComponent<player_movemen>();
    }

    private void OnDisable()
    {
        Vertical = 0f;
        hitQueued = false;
    }

    private void Update()
    {
        ball_controller ball = player.Ball;
        if (ball == null)
        {
            Vertical = 0f;
            return;
        }

        if (ball.LastHitter != trackedHitter || ball.State != trackedState)
            OnNewShot(ball);

        // By default, drift back to the middle of the table.
        float targetY = ball.TableBounds.center.y;

        switch (ball.State)
        {
            case ball_controller.BallState.Held:
                if (ball.Server == player)
                {
                    targetY = player.PaddlePoint.y; // stand still while serving
                    serveTimer -= Time.deltaTime;
                    if (serveTimer <= 0f) PressHit();
                }
                break;

            case ball_controller.BallState.InPlay:
                if (ball.LastHitter != player)
                    targetY = TrackIncoming(ball);
                break;
        }

        MoveToward(targetY);
    }

    private void OnNewShot(ball_controller ball)
    {
        trackedHitter = ball.LastHitter;
        trackedState = ball.State;

        reactionTimer = reactionDelay;
        serveTimer = serveDelay;
        errorY = Random.Range(-positionError, positionError);
        willMiss = Random.value < missChance;
        swung = false;
        hitQueued = false;
        Aim = Random.value < aimChance ? (Random.value < 0.5f ? -1f : 1f) : 0f;
    }

    /// <summary>Returns the Y the paddle should move to, and swings when the ball is about to arrive.</summary>
    private float TrackIncoming(ball_controller ball)
    {
        Vector2 paddle = player.PaddlePoint;

        if (reactionTimer > 0f)
        {
            reactionTimer -= Time.deltaTime;
            return paddle.y; // still "reacting", hold position
        }

        // Hits are judged on where the ball LOOKS like it is, so predict that (bounces included)
        // for the moment it reaches our paddle's X. If it's already past us, just chase it.
        float predictedY, timeToPaddle;
        if (ball.PredictVisualAtX(paddle.x, out Vector2 visual, out float t))
        {
            predictedY = visual.y;
            timeToPaddle = t;
        }
        else
        {
            predictedY = ball.VisualPosition.y;
            timeToPaddle = 0f;
        }

        // Swing a little early - player_movemen waits for the ball's closest point within the swing.
        if (!willMiss && timeToPaddle <= player.SwingDuration * 0.5f)
            PressHit();

        return predictedY + errorY;
    }

    private void PressHit()
    {
        if (swung) return;
        swung = true;
        hitQueued = true;
    }

    private void MoveToward(float targetY)
    {
        float diff = targetY - player.PaddlePoint.y;
        Vertical = Mathf.Abs(diff) <= deadZone
            ? 0f
            : Mathf.Clamp(diff / Mathf.Max(0.01f, fullSpeedDistance), -1f, 1f);
    }
}
