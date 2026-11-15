using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

/// <summary>
/// The special shot sequence. Add this to a player (next to player_movemen). Each player can have their own.
///
///   1. The special hit sends the ball, then TIME STOPS.
///   2. The cutscene plays (a Timeline, or a GameObject that's switched on for a set time).
///   3. Time stays stopped while the player's sprite swaps to a pose.
///   4. Time resumes with the ball faster. That speed is the ball's minimum until the match resets.
///
/// Everything here runs on real (unscaled) time, since game time is frozen.
/// </summary>
[RequireComponent(typeof(player_movemen))]
public class special_ability : MonoBehaviour
{
    [Header("Cutscene (use one, or leave both empty to just pause)")]
    [Tooltip("A Timeline to play. It's switched to unscaled time automatically so it runs while time is stopped.")]
    [SerializeField] private PlayableDirector cutsceneTimeline;
    [Tooltip("An object switched on for the cutscene, then off again. If it has a Video Player, the video is played " +
             "and the cutscene lasts exactly as long as the clip. Otherwise it stays on for Cutscene Duration " +
             "(Animators on it need Update Mode = Unscaled Time; animation_controllers need Use Unscaled Time ticked).")]
    [SerializeField] private GameObject cutsceneObject;
    [Tooltip("Real seconds the cutscene lasts when it ISN'T a video (or the pause length if no cutscene is set). " +
             "Videos use their own clip length.")]
    [SerializeField] private float cutsceneDuration = 1.5f;

    [Header("Pose (after the cutscene, time still stopped)")]
    [Tooltip("Sprite the player switches to. Empty = skip the pose.")]
    [SerializeField] private Sprite poseSprite;
    [Tooltip("Real seconds to hold the pose before time resumes.")]
    [SerializeField] private float poseDuration = 0.75f;

    [Header("Ball")]
    [Tooltip("Each special multiplies the ball's speed by this (1.5 = 50% faster). Stacks if specials happen again " +
             "before the reset; the ball never drops below its boosted speed until then.")]
    [SerializeField] private float ballSpeedMultiplier = 1.5f;

    /// <summary>True while the time-stop sequence is running.</summary>
    public bool IsPlaying { get; private set; }

    private player_movemen player;
    private SpriteRenderer spriteRenderer;
    private Sprite spriteBeforePose;
    private bool posing;
    private Coroutine sequence;

    private void Awake()
    {
        player = GetComponent<player_movemen>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (cutsceneObject != null) cutsceneObject.SetActive(false);
    }

    /// <summary>Starts the special sequence. Called by player_movemen right after the special hit.</summary>
    public void Activate()
    {
        Cancel();
        sequence = StartCoroutine(Sequence());
    }

    /// <summary>Stops the sequence immediately and puts everything back (used by match resets).</summary>
    public void Cancel()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;

        if (cutsceneTimeline != null && cutsceneTimeline.state == PlayState.Playing) cutsceneTimeline.Stop();
        if (cutsceneObject != null)
        {
            VideoPlayer video = cutsceneObject.GetComponentInChildren<VideoPlayer>();
            if (video != null) video.Stop();
            cutsceneObject.SetActive(false);
        }
        EndPose();

        if (IsPlaying) match_effects.Instance.ResumeTime();
        IsPlaying = false;
    }

    private IEnumerator Sequence()
    {
        IsPlaying = true;

        // 1. Stop time.
        match_effects.Instance.StopTime();

        // 2. Cutscene.
        if (cutsceneTimeline != null)
        {
            cutsceneTimeline.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            cutsceneTimeline.extrapolationMode = DirectorWrapMode.None;
            cutsceneTimeline.time = 0;
            cutsceneTimeline.Play();

            float timeout = (float)cutsceneTimeline.duration + 0.5f;
            float t = 0f;
            yield return null; // let it start
            while (cutsceneTimeline.state == PlayState.Playing && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            cutsceneTimeline.Stop();
        }
        else if (cutsceneObject != null)
        {
            cutsceneObject.SetActive(true);

            VideoPlayer video = cutsceneObject.GetComponentInChildren<VideoPlayer>();
            if (video != null)
                yield return PlayVideo(video);
            else
                yield return new WaitForSecondsRealtime(cutsceneDuration);

            cutsceneObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSecondsRealtime(cutsceneDuration);
        }

        // 3. Pose, still frozen.
        if (poseSprite != null && spriteRenderer != null)
        {
            spriteBeforePose = spriteRenderer.sprite;
            spriteRenderer.sprite = poseSprite;
            posing = true;
            yield return new WaitForSecondsRealtime(poseDuration);
            EndPose();
        }

        // 4. Resume with a faster ball.
        if (player.Ball != null) player.Ball.StackSpeedBoost(ballSpeedMultiplier);
        match_effects.Instance.QueueTableMark(); // the next table bounce leaves a mark
        match_effects.Instance.ResumeTime();

        IsPlaying = false;
        sequence = null;
    }

    /// <summary>Plays the cutscene video from the start and waits exactly as long as the clip is.</summary>
    private IEnumerator PlayVideo(VideoPlayer video)
    {
        video.playOnAwake = false;
        video.isLooping = false;
        video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime; // keep playing while time is stopped

        // Load the video first so it starts instantly on its first frame.
        video.Prepare();
        float waited = 0f;
        while (!video.isPrepared && waited < 3f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        // Length comes from the clip itself, so swapping the video needs no other changes.
        double length = video.clip != null ? video.clip.length : video.length;
        if (length <= 0) length = cutsceneDuration;

        bool finished = false;
        VideoPlayer.EventHandler onEnd = _ => finished = true;
        video.loopPointReached += onEnd;

        video.time = 0;
        video.Play();

        // Wait for the clip to end (with a small safety margin in case the end event never fires).
        float elapsed = 0f;
        while (!finished && elapsed < length + 0.5f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        video.loopPointReached -= onEnd;
        video.Stop();
    }

    private void EndPose()
    {
        if (!posing) return;
        posing = false;
        if (spriteRenderer != null) spriteRenderer.sprite = spriteBeforePose;
    }
}
