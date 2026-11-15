using TMPro;
using UnityEngine;

/// <summary>
/// Keeps the on-screen score texts in sync with each player's points.
/// The number pops briefly when it changes.
///
/// Add this anywhere (e.g. on the Canvas). If the slots are left empty it finds the players by Player Number,
/// and the texts by the object names "score p1" / "score p2".
/// </summary>
public class score_display : MonoBehaviour
{
    [Header("Players (found by Player Number if empty)")]
    [SerializeField] private player_movemen player1;
    [SerializeField] private player_movemen player2;

    [Header("Texts (found by name 'score p1' / 'score p2' if empty)")]
    [SerializeField] private TMP_Text player1Text;
    [SerializeField] private TMP_Text player2Text;

    [Header("Pop When Scoring")]
    [SerializeField] private float popScale = 1.35f;
    [Tooltip("Real seconds the pop takes to settle.")]
    [SerializeField] private float popDuration = 0.25f;

    private int shown1 = -1, shown2 = -1;
    private float pop1, pop2;
    private Vector3 baseScale1 = Vector3.one, baseScale2 = Vector3.one;

    private void Start()
    {
        foreach (player_movemen p in FindObjectsByType<player_movemen>())
        {
            if (player1 == null && p.PlayerNumber == 1) player1 = p;
            else if (player2 == null && p.PlayerNumber == 2) player2 = p;
        }

        if (player1Text == null) player1Text = FindText("score p1");
        if (player2Text == null) player2Text = FindText("score p2");

        if (player1Text != null) baseScale1 = player1Text.transform.localScale;
        if (player2Text != null) baseScale2 = player2Text.transform.localScale;

        Refresh(force: true);
    }

    private void Update()
    {
        Refresh(force: false);
        AnimatePop(player1Text, ref pop1, baseScale1);
        AnimatePop(player2Text, ref pop2, baseScale2);
    }

    /// <summary>Only touches the text when a score actually changed (scoring, or a match reset back to 0).</summary>
    private void Refresh(bool force)
    {
        int s1 = player1 != null ? player1.Points : 0;
        int s2 = player2 != null ? player2.Points : 0;

        if (force || s1 != shown1)
        {
            if (player1Text != null) player1Text.text = s1.ToString();
            if (!force && s1 > shown1) pop1 = popDuration;
            shown1 = s1;
        }

        if (force || s2 != shown2)
        {
            if (player2Text != null) player2Text.text = s2.ToString();
            if (!force && s2 > shown2) pop2 = popDuration;
            shown2 = s2;
        }
    }

    private void AnimatePop(TMP_Text text, ref float timer, Vector3 baseScale)
    {
        if (text == null || timer <= 0f) return;

        timer -= Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.01f, popDuration)); // 1 -> 0
        text.transform.localScale = baseScale * Mathf.Lerp(1f, popScale, t * t);
    }

    private static TMP_Text FindText(string objectName)
    {
        foreach (TMP_Text t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
            if (t.gameObject.name == objectName) return t;

        Debug.LogWarning($"[score_display] Couldn't find a text object named '{objectName}'. Assign it in the Inspector.");
        return null;
    }
}
