using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Plays when a player reaches the winning score:
///   1. Both players stop responding to input.
///   2. The winner glides to the center of the screen while the loser fades out.
///   3. The wave transition swirls out from the center (slower than usual) BEHIND the winner,
///      covering the table so the winner stands alone on it. The score stays visible on top.
///   4. Once the winner is centered, any key / click / controller button returns to the menu
///      (the winner fades into the wave, then the normal wave transition loads the menu).
///
/// Triggered by ball_controller. Optional to place in the scene to tweak it; created automatically if missing.
/// </summary>
public class round_end : MonoBehaviour
{
    private static round_end instance;

    public static round_end Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<round_end>();
                if (instance == null) instance = new GameObject("round_end").AddComponent<round_end>();
            }
            return instance;
        }
    }

    [SerializeField] private string menuSceneName = "mENU";
    [Tooltip("Seconds for the winner to glide to the center.")]
    [SerializeField] private float moveDuration = 1f;
    [Tooltip("Seconds for the loser to fade out.")]
    [SerializeField] private float loserFadeDuration = 0.8f;
    [Tooltip("Seconds for the victory wave to cover the screen (the cutscene wave is 0.35).")]
    [SerializeField] private float waveDuration = 1.4f;
    [Tooltip("Seconds for the winner to fade into the wave after a key is pressed.")]
    [SerializeField] private float winnerFadeDuration = 0.25f;
    [Tooltip("Draw order for the winner so they sit on top of the wave and everything else.")]
    [SerializeField] private int winnerSortingOrder = 500;

    public bool IsPlaying { get; private set; }

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

    public void Play(player_movemen winner, player_movemen loser)
    {
        if (IsPlaying || winner == null) return;
        StartCoroutine(Sequence(winner, loser));
    }

    private IEnumerator Sequence(player_movemen winner, player_movemen loser)
    {
        IsPlaying = true;
        match_effects.Instance.ResetTime();

        // Freeze both players and tidy away their gauges.
        foreach (player_movemen p in new[] { winner, loser })
        {
            if (p == null) continue;
            p.ControlsLocked = true;
            if (p.TryGetComponent(out special_ability ability)) ability.Cancel();
            if (p.TryGetComponent(out special_gauge_display gauge)) gauge.enabled = false;
        }

        // Winner draws above everything in the world; the wave goes right underneath them.
        Camera cam = Camera.main;
        SpriteRenderer winnerSprite = winner.GetComponent<SpriteRenderer>();
        int layer = winnerSprite != null ? winnerSprite.sortingLayerID : 0;
        if (winnerSprite != null) winnerSprite.sortingOrder = winnerSortingOrder;

        cutscene_transition wave = cutscene_transition.Instance;
        wave.DrawBehind(cam, layer, winnerSortingOrder - 1);

        // All at once: loser fades, winner glides to center, wave swirls out from the center (slowly).
        if (loser != null) StartCoroutine(Fade(loser.gameObject, loserFadeDuration));
        bool waveDone = false;
        StartCoroutine(RunThen(wave.Cover(new Vector2(0.5f, 0.5f), waveDuration), () => waveDone = true));

        Vector3 start = winner.transform.position;
        Vector3 center = start;
        if (cam != null)
        {
            Vector3 c = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, Mathf.Abs(cam.transform.position.z - start.z)));
            center = new Vector3(c.x, c.y, start.z);
        }

        float t = 0f;
        while (t < moveDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, moveDuration));
            k = k * k * (3f - 2f * k); // ease in-out
            winner.transform.position = Vector3.Lerp(start, center, k);
            yield return null;
        }
        winner.transform.position = center;

        // Winner is centered: wait for the wave to finish, then for any key.
        while (!waveDone) yield return null;
        yield return null; // don't count a key that was already held this frame
        while (!AnyInputPressed()) yield return null;

        // Winner fades into the wave, then back to the menu with the normal transition.
        yield return Fade(winner.gameObject, winnerFadeDuration);
        wave.DrawOverEverything(); // already fully covered, now also over the UI
        wave.TransitionToScene(menuSceneName, new Vector2(0.5f, 0.5f));
    }

    private static IEnumerator RunThen(IEnumerator routine, System.Action done)
    {
        yield return routine;
        done();
    }

    /// <summary>Fades every sprite on an object (and its children) to invisible.</summary>
    private static IEnumerator Fade(GameObject target, float duration)
    {
        SpriteRenderer[] sprites = target.GetComponentsInChildren<SpriteRenderer>();
        Color[] start = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) start[i] = sprites[i].color;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color c = start[i];
                c.a *= a;
                sprites[i].color = c;
            }
            yield return null;
        }

        foreach (SpriteRenderer sr in sprites)
            if (sr != null) sr.enabled = false;
    }

    private static bool AnyInputPressed()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
        if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) return true;

        Gamepad pad = Gamepad.current;
        if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                            pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame ||
                            pad.startButton.wasPressedThisFrame))
            return true;

        return false;
    }
}
