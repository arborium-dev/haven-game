// some ai, due to time chaanges

using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("Flip the cutscene left-to-right when this player is on the right side of the table, so it faces " +
             "the same way they do. Works for videos and cutscene objects (not Timelines).")]
    [SerializeField] private bool mirrorOnRightSide = true;

    [Header("Music")]
    [Tooltip("Music to pause when the cutscene starts. Leave empty to pause every AudioSource that's playing " +
             "at that moment (the cutscene's own audio is left alone).")]
    [SerializeField] private AudioSource[] musicSources;
    [Tooltip("ON: music picks up where it left off when time resumes. OFF: it stays stopped.")]
    [SerializeField] private bool resumeMusicAfter = true;

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
    private readonly List<AudioSource> pausedAudio = new List<AudioSource>();
    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    // Mirroring state (restored by EndMirror).
    private VideoPlayer mirroredVideo;
    private VideoRenderMode savedRenderMode;
    private RenderTexture savedTargetTexture;
    private RenderTexture mirrorTexture;
    private GameObject mirrorScreen;
    private bool objectFlipped;
    private Vector3 savedObjectScale;

    private bool MirrorCutscene => mirrorOnRightSide && player.Side > 0;

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
            EndMirror();
            cutsceneObject.SetActive(false);
        }
        EndPose();

        if (IsPlaying) cutscene_transition.Instance.HideInstant();
        ShowUI();

        if (IsPlaying) match_effects.Instance.ResumeTime();
        ResumeMusic();
        IsPlaying = false;
    }

    private void PauseMusic()
    {
        pausedAudio.Clear();

        AudioSource[] sources = musicSources != null && musicSources.Length > 0
            ? musicSources
            : FindObjectsByType<AudioSource>();

        foreach (AudioSource source in sources)
        {
            if (source == null || !source.isPlaying) continue;
            // Don't silence the cutscene's own sound.
            if (cutsceneObject != null && source.transform.IsChildOf(cutsceneObject.transform)) continue;

            source.Pause();
            pausedAudio.Add(source);
        }
    }

    private void ResumeMusic()
    {
        if (resumeMusicAfter)
        {
            foreach (AudioSource source in pausedAudio)
                if (source != null) source.UnPause();
        }
        pausedAudio.Clear();
    }

    private IEnumerator Sequence()
    {
        IsPlaying = true;

        // Cutscenes can be switched off from the menu: then the special skips straight to the pose + speed up.
        bool cutscenesOn = game_settings.CutscenesEnabled;

        // 1. Stop time and the music.
        match_effects.Instance.StopTime();
        if (cutscenesOn) PauseMusic();

        // 2. Cutscene, wrapped in the wave transition.
        bool hasCutscene = cutscenesOn && (cutsceneTimeline != null || cutsceneObject != null);
        cutscene_transition wave = cutscene_transition.Instance;

        if (hasCutscene)
        {
            // A wave swirls out from this player and covers everything, then the UI is hidden underneath it.
            wave.DrawOverEverything();
            yield return wave.Cover(ViewportPositionOfPlayer());
            HideUI();
        }

        if (!cutscenesOn)
        {
            // No cutscene, no pause.
        }
        else if (cutsceneTimeline != null)
        {
            cutsceneTimeline.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            cutsceneTimeline.extrapolationMode = DirectorWrapMode.None;
            cutsceneTimeline.time = 0;
            cutsceneTimeline.Play();

            yield return null; // let it start
            wave.HideInstant();

            float timeout = (float)cutsceneTimeline.duration + 0.5f;
            float t = 0f;
            while (cutsceneTimeline.state == PlayState.Playing && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            wave.ShowCovered(); // cover the last frame before the cutscene disappears
            cutsceneTimeline.Stop();
        }
        else if (cutsceneObject != null)
        {
            cutsceneObject.SetActive(true);
            bool mirror = MirrorCutscene;

            VideoPlayer video = cutsceneObject.GetComponentInChildren<VideoPlayer>();
            if (video != null)
            {
                yield return PlayVideo(video, mirror);
            }
            else
            {
                if (mirror) FlipCutsceneObject();
                yield return null;
                wave.HideInstant();
                yield return new WaitForSecondsRealtime(cutsceneDuration);
            }

            wave.ShowCovered(); // cover the last frame before the cutscene disappears
            if (video != null) video.Stop();
            EndMirror();
            cutsceneObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSecondsRealtime(cutsceneDuration);
        }

        if (hasCutscene)
        {
            // Bring the UI back under the wave, then pull the wave back to reveal the game.
            ShowUI();
            yield return wave.Uncover();
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
        ResumeMusic();

        IsPlaying = false;
        sequence = null;
    }

    /// <summary>Plays the cutscene video from the start and waits exactly as long as the clip is.</summary>
    private IEnumerator PlayVideo(VideoPlayer video, bool mirror)
    {
        video.playOnAwake = false;
        video.isLooping = false;
        video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime; // keep playing while time is stopped

        if (mirror) BeginMirroredVideo(video);

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

        // Keep the wave up until the video's first frame is actually on screen, then drop it.
        float startWait = 0f;
        while (video.frame <= 0 && startWait < 0.5f)
        {
            startWait += Time.unscaledDeltaTime;
            yield return null;
        }
        cutscene_transition.Instance.HideInstant();

        // Wait for the clip to end (with a small safety margin in case the end event never fires).
        float elapsed = startWait;
        while (!finished && elapsed < length + 0.5f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        video.loopPointReached -= onEnd;
        // (Stopped by Sequence once the wave has covered the screen again.)
    }

    // ---------- Mirroring ----------

    /// <summary>
    /// Camera-drawn videos can't be flipped, so for the mirrored version the video plays into a texture that's
    /// shown full screen, flipped left-to-right, sized the same way the VideoPlayer's Aspect Ratio setting would.
    /// </summary>
    private void BeginMirroredVideo(VideoPlayer video)
    {
        mirroredVideo = video;
        savedRenderMode = video.renderMode;
        savedTargetTexture = video.targetTexture;

        int w = video.clip != null ? (int)video.clip.width : 1920;
        int h = video.clip != null ? (int)video.clip.height : 1080;
        mirrorTexture = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), 0) { name = "MirroredCutscene" };
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = mirrorTexture;

        mirrorScreen = new GameObject("MirroredCutscene");
        var canvas = mirrorScreen.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000; // above the game UI, just under the wave transition

        var imageObj = new GameObject("Video", typeof(RectTransform));
        imageObj.transform.SetParent(mirrorScreen.transform, false);
        var rt = (RectTransform)imageObj.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = FitSize(video.aspectRatio, w, h, Screen.width, Screen.height);

        var image = imageObj.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = mirrorTexture;
        image.uvRect = new Rect(1f, 0f, -1f, 1f); // flipped horizontally
        image.raycastTarget = false;
    }

    private void FlipCutsceneObject()
    {
        savedObjectScale = cutsceneObject.transform.localScale;
        cutsceneObject.transform.localScale = new Vector3(-savedObjectScale.x, savedObjectScale.y, savedObjectScale.z);
        objectFlipped = true;
    }

    /// <summary>Puts the video / cutscene object back exactly how it was set up.</summary>
    private void EndMirror()
    {
        if (mirroredVideo != null)
        {
            mirroredVideo.targetTexture = savedTargetTexture;
            mirroredVideo.renderMode = savedRenderMode;
            mirroredVideo = null;
        }
        if (mirrorScreen != null) Destroy(mirrorScreen);
        mirrorScreen = null;
        if (mirrorTexture != null)
        {
            mirrorTexture.Release();
            Destroy(mirrorTexture);
            mirrorTexture = null;
        }
        if (objectFlipped && cutsceneObject != null)
        {
            cutsceneObject.transform.localScale = savedObjectScale;
            objectFlipped = false;
        }
    }

    /// <summary>The on-screen size a video gets for each VideoPlayer Aspect Ratio mode.</summary>
    private static Vector2 FitSize(VideoAspectRatio mode, float vw, float vh, float sw, float sh)
    {
        switch (mode)
        {
            case VideoAspectRatio.NoScaling: return new Vector2(vw, vh);
            case VideoAspectRatio.FitVertically: return new Vector2(sh * vw / vh, sh);
            case VideoAspectRatio.FitHorizontally: return new Vector2(sw, sw * vh / vw);
            case VideoAspectRatio.FitInside:
            {
                float s = Mathf.Min(sw / vw, sh / vh);
                return new Vector2(vw * s, vh * s);
            }
            case VideoAspectRatio.FitOutside:
            {
                float s = Mathf.Max(sw / vw, sh / vh);
                return new Vector2(vw * s, vh * s);
            }
            default: return new Vector2(sw, sh); // Stretch
        }
    }

    private Vector2 ViewportPositionOfPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null) return new Vector2(0.5f, 0.5f);
        Vector3 v = cam.WorldToViewportPoint(transform.position);
        return new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y));
    }

    /// <summary>Hides screen UI (score etc.) during the cutscene so it doesn't draw on top of the video.</summary>
    private void HideUI()
    {
        hiddenCanvases.Clear();
        Canvas waveCanvas = cutscene_transition.Instance.OverlayCanvas;

        foreach (Canvas c in FindObjectsByType<Canvas>())
        {
            if (c == null || !c.enabled || !c.isRootCanvas || c == waveCanvas) continue;
            if (c.renderMode == RenderMode.WorldSpace) continue; // in-world UI is already covered by the video
            if (cutsceneObject != null && c.transform.IsChildOf(cutsceneObject.transform)) continue;

            c.enabled = false;
            hiddenCanvases.Add(c);
        }
    }

    private void ShowUI()
    {
        foreach (Canvas c in hiddenCanvases)
            if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
    }

    private void EndPose()
    {
        if (!posing) return;
        posing = false;
        if (spriteRenderer != null) spriteRenderer.sprite = spriteBeforePose;
    }
}
