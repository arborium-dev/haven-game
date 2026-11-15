using System.Collections;
using UnityEngine;

/// <summary>
/// Game music, in one of two modes (picked with the SPLIT MUSIC toggle on the menu):
///
///   OFF (one song): does nothing - the music plays exactly as set up on the AudioSource, start to finish.
///   ON  (split):    separate tracks for each stage of the game, crossfading between them:
///                     Tutorial Music     - while the tutorial is running
///                     Main Music         - the game itself
///                     Post Ability Music - once a special has been used, until that point is won
///                                          (only with cutscenes ON; with cutscenes off it stays on Main Music)
///                     Win Music          - once someone has won the round, until back at the menu
///                   Any empty slot falls back to the AudioSource's normal song (or main music, for post-ability).
///
/// Add this to the music object (the one with the music AudioSource) in the Gameplay scene.
/// </summary>
public class music_manager : MonoBehaviour
{
    [Tooltip("The music player. Empty = the AudioSource on this object.")]
    [SerializeField] private AudioSource musicSource;

    [Header("Split Music Tracks (only used when SPLIT MUSIC is ticked on the menu)")]
    [Tooltip("Plays during the tutorial. Empty = the AudioSource's normal song.")]
    [SerializeField] private AudioClip tutorialMusic;
    [Tooltip("Plays during the game. Empty = the AudioSource's normal song.")]
    [SerializeField] private AudioClip mainMusic;
    [Tooltip("Plays once a special has been used, until that point is won. Never used when cutscenes are off. " +
             "Empty = keep playing the main music.")]
    [SerializeField] private AudioClip postAbilityMusic;
    [Tooltip("Plays once someone has won the round (through the victory screen until back at the menu). " +
             "Empty = whatever was playing keeps going.")]
    [SerializeField] private AudioClip winMusic;
    [Tooltip("Seconds to crossfade between tracks.")]
    [SerializeField] private float fadeTime = 0.8f;

    private AudioClip originalClip;
    private float baseVolume = 1f;
    private ball_controller ball;
    private bool abilityUsed;
    private bool wasPowered;
    private AudioClip targetClip;
    private Coroutine fade;

    private AudioClip TutorialTrack => tutorialMusic != null ? tutorialMusic : originalClip;
    private AudioClip MainTrack => mainMusic != null ? mainMusic : originalClip;
    private AudioClip PostAbilityTrack => postAbilityMusic != null ? postAbilityMusic : MainTrack;

    private void Awake()
    {
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
        {
            Debug.LogWarning($"[music_manager] No AudioSource on '{name}' to play music with.", this);
            enabled = false;
            return;
        }

        originalClip = musicSource.clip;
        baseVolume = musicSource.volume;

        // One-song mode: leave the music exactly as it is.
        if (!game_settings.SplitMusic)
        {
            enabled = false;
            return;
        }

        // Start on the right track straight away, so the scene's normal song never blips in first.
        SwitchTo(game_settings.TutorialEnabled ? TutorialTrack : MainTrack, instant: true);
    }

    private void Update()
    {
        if (ball == null)
        {
            foreach (ball_controller b in FindObjectsByType<ball_controller>())
                if (!b.PracticeMode) { ball = b; break; }
            if (ball == null) return;
            ball.OnPointScored += HandlePointScored;
        }

        // A special just powered up the ball: switch to the post-ability music (until the point is over).
        // With cutscenes switched off, there's no special music at all: the main music just keeps playing.
        bool powered = ball.IsPowered;
        if (powered && !wasPowered && game_settings.CutscenesEnabled) abilityUsed = true;
        wasPowered = powered;

        AudioClip want = ball.RoundOver && winMusic != null ? winMusic
                       : ball.WaitingToStart ? TutorialTrack
                       : abilityUsed ? PostAbilityTrack
                       : MainTrack;

        if (want != targetClip) SwitchTo(want, instant: false);
    }

    /// <summary>A point was won: back to the main music.</summary>
    private void HandlePointScored(player_movemen winner) => abilityUsed = false;

    private void OnDestroy()
    {
        if (ball != null) ball.OnPointScored -= HandlePointScored;
    }

    private void SwitchTo(AudioClip clip, bool instant)
    {
        targetClip = clip;
        if (fade != null) StopCoroutine(fade);
        fade = null;

        if (instant || !musicSource.isPlaying)
        {
            musicSource.volume = baseVolume;
            if (musicSource.clip != clip || !musicSource.isPlaying)
            {
                musicSource.clip = clip;
                if (clip != null) musicSource.Play();
            }
            return;
        }

        if (musicSource.clip == clip) return; // already playing this one
        fade = StartCoroutine(Crossfade(clip));
    }

    /// <summary>Fade the current track out, swap, fade the new one in (real time, so it works during time stops).</summary>
    private IEnumerator Crossfade(AudioClip clip)
    {
        float half = Mathf.Max(0.01f, fadeTime * 0.5f);
        float startVolume = musicSource.volume;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(startVolume, 0f, t / half);
            yield return null;
        }
        musicSource.volume = 0f;

        // Don't start the new song in the middle of a special's cutscene (the music is paused then).
        while (Time.timeScale == 0f) yield return null;

        musicSource.clip = clip;
        if (clip != null) musicSource.Play();

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, baseVolume, t / half);
            yield return null;
        }
        musicSource.volume = baseVolume;
        fade = null;
    }
}
