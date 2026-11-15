using UnityEngine;

/// <summary>
/// The player's hit animation: they hold the rocket launcher by its grip and swing it like a paddle every time
/// they swing (wind up, whip through, settle back). Mirrors automatically for the player on the right side.
///
/// Add this to each player (next to player_movemen). With no sprite assigned it loads "Art/bazooka" from Resources.
/// The launcher is a child of the player, so it inherits the player's scale and matches their pixel size.
/// </summary>
[RequireComponent(typeof(player_movemen))]
public class paddle_swing : MonoBehaviour
{
    [Header("Launcher Sprite")]
    [Tooltip("The rocket launcher. Empty = loads the 'bazooka' sprite from Resources/Art. " +
             "It rotates around the sprite's pivot, so put the pivot on the grip.")]
    [SerializeField] private Sprite launcherSprite;
    [Tooltip("Tick if the barrel points right in the sprite image (untick if it points left).")]
    [SerializeField] private bool spriteFacesRight = true;
    [Tooltip("Where the grip is held, in world units from the player's center. X flips to face the table.")]
    [SerializeField] private Vector2 holdOffset = new Vector2(0.35f, 0.25f);
    [Tooltip("Size relative to the player's pixels (1 = same pixel size as the player sprite).")]
    [SerializeField] private float scale = 1f;
    [Tooltip("Draw order relative to the player (+1 = in front).")]
    [SerializeField] private int sortingOrderOffset = 1;
    [Tooltip("Hide the launcher except while swinging.")]
    [SerializeField] private bool onlyShowWhileSwinging = false;

    [Header("Swing (angles for a player facing right; mirrored for the left-facing player)")]
    [Tooltip("Resting angle between swings (0 = barrel pointing at the table, 90 = straight up).")]
    [SerializeField] private float restAngle = 35f;
    [Tooltip("Pulled back before the strike.")]
    [SerializeField] private float windupAngle = 110f;
    [Tooltip("Where the swing ends up after whipping through.")]
    [SerializeField] private float followThroughAngle = -55f;
    [SerializeField] private float windupTime = 0.05f;
    [SerializeField] private float strikeTime = 0.08f;
    [SerializeField] private float holdTime = 0.05f;
    [SerializeField] private float recoverTime = 0.18f;

    private player_movemen player;
    private SpriteRenderer playerSprite;
    private Transform launcher;
    private SpriteRenderer launcherRenderer;
    private float swingTime = -1f; // < 0 = not swinging

    /// <summary>True while the swing animation is playing.</summary>
    public bool IsSwinging => swingTime >= 0f;

    private void Awake()
    {
        player = GetComponent<player_movemen>();
        playerSprite = GetComponent<SpriteRenderer>();

        if (launcherSprite == null)
        {
            Sprite[] found = Resources.LoadAll<Sprite>("Art/bazooka");
            if (found.Length > 0) launcherSprite = found[0];
            else Debug.LogWarning($"[paddle_swing] No launcher sprite on '{name}' and none found at Resources/Art/bazooka.", this);
        }

        var obj = new GameObject("Launcher");
        obj.transform.SetParent(transform, false);
        launcher = obj.transform;
        launcherRenderer = obj.AddComponent<SpriteRenderer>();
        launcherRenderer.sprite = launcherSprite;
        if (playerSprite != null) launcherRenderer.sharedMaterial = playerSprite.sharedMaterial;
    }

    private void OnEnable() => player.OnSwing += StartSwing;
    private void OnDisable() => player.OnSwing -= StartSwing;

    private void StartSwing() => swingTime = 0f;

    private void LateUpdate()
    {
        int facing = player.Facing >= 0 ? 1 : -1;

        // Hold point, converted from world units to the player's (scaled) local space.
        Vector3 s = transform.lossyScale;
        launcher.localPosition = new Vector3(
            holdOffset.x * facing / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
            holdOffset.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
            0f);
        launcher.localScale = Vector3.one * scale;

        // Swing angle (scaled time, so it freezes with time stops).
        float angle = restAngle;
        if (swingTime >= 0f)
        {
            swingTime += Time.deltaTime;
            angle = SampleSwing(swingTime, out bool finished);
            if (finished) swingTime = -1f;
        }

        // Mirror for the player facing left.
        launcher.localRotation = Quaternion.Euler(0f, 0f, facing > 0 ? angle : -angle);
        launcherRenderer.flipX = (facing < 0) == spriteFacesRight;

        // Stay just in front of the player, even when their draw order changes (e.g. the round-end winner).
        if (playerSprite != null)
        {
            launcherRenderer.sortingLayerID = playerSprite.sortingLayerID;
            launcherRenderer.sortingOrder = playerSprite.sortingOrder + sortingOrderOffset;
        }

        if (onlyShowWhileSwinging) launcherRenderer.enabled = IsSwinging;
    }

    /// <summary>Wind up (ease out), whip through (ease in, fast), hold a beat, then settle back to rest.</summary>
    private float SampleSwing(float t, out bool finished)
    {
        finished = false;
        float a = windupTime, b = a + strikeTime, c = b + holdTime, d = c + recoverTime;

        if (t < a)
        {
            float k = t / Mathf.Max(0.0001f, windupTime);
            return Mathf.Lerp(restAngle, windupAngle, 1f - (1f - k) * (1f - k));
        }
        if (t < b)
        {
            float k = (t - a) / Mathf.Max(0.0001f, strikeTime);
            return Mathf.Lerp(windupAngle, followThroughAngle, k * k);
        }
        if (t < c) return followThroughAngle;
        if (t < d)
        {
            float k = (t - c) / Mathf.Max(0.0001f, recoverTime);
            return Mathf.Lerp(followThroughAngle, restAngle, k * k * (3f - 2f * k));
        }

        finished = true;
        return restAngle;
    }
}
