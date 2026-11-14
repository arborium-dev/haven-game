using System;
using System.Collections.Generic;
using UnityEngine;

public class animation_controller : MonoBehaviour
{
    [Serializable]
    public class SpriteAnimation
    {
        [Tooltip("Name used to play this animation. If left blank, the first sprite sheet's name is used.")]
        public string name;

        [Tooltip("Speed of this animation in frames per second.")]
        public float framesPerSecond = 12f;

        [Tooltip("Should this animation loop when it reaches the end?")]
        public bool loop = true;

        [Tooltip("Drag 1 or more sliced Sprite Sheet textures here (auto-fills Frames below).")]
        public List<Texture2D> spriteSheets = new List<Texture2D>();

        [Tooltip("Individual sprite frames (auto-filled from Sprite Sheets, or drag Sprites here directly).")]
        public List<Sprite> frames = new List<Sprite>();
    }

    [Header("Renderer")]
    [Tooltip("Optional. If left empty, it automatically finds or adds a SpriteRenderer on this object.")]
    [SerializeField] private SpriteRenderer targetRenderer;

    [Header("Playback Settings")]
    [SerializeField] private bool playOnStart = true;

    [Tooltip("Optional starting animation name. If blank, plays the first animation in the list.")]
    [SerializeField] private string startingAnimation = "";

    [Tooltip("Global speed multiplier applied to all animations (1 = normal, 2 = 2x speed, 0 = paused).")]
    [SerializeField] private float speedMultiplier = 1f;

    [Header("Animations")]
    public List<SpriteAnimation> animations = new List<SpriteAnimation>();

    /// <summary>Fires when a non-looping animation reaches its last frame.</summary>
    public event Action<string> OnAnimationFinished;

    public string CurrentAnimationName =>
        (currentAnimIndex >= 0 && currentAnimIndex < animations.Count)
            ? GetAnimationName(animations[currentAnimIndex])
            : string.Empty;

    public int CurrentAnimationIndex => currentAnimIndex;
    public bool IsPlaying => isPlaying;
    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = value;
    }

    private int currentAnimIndex = -1;
    private int currentFrame;
    private float timer;
    private bool isPlaying;

    private void Reset()
    {
        CacheRenderer();
    }

    private void Awake()
    {
        CacheRenderer();
    }

    private void Start()
    {
        // Only auto-play if another script didn't already call Play() during Awake/Start
        if (playOnStart && currentAnimIndex == -1 && animations.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(startingAnimation))
                Play(startingAnimation);
            else
                Play(0);
        }
    }

    private void Update()
    {
        if (!isPlaying || currentAnimIndex < 0 || currentAnimIndex >= animations.Count)
            return;

        SpriteAnimation anim = animations[currentAnimIndex];
        if (anim.frames == null || anim.frames.Count == 0)
            return;

        float effectiveFps = anim.framesPerSecond * speedMultiplier;
        if (Mathf.Approximately(effectiveFps, 0f))
            return;

        float frameDuration = 1f / Mathf.Abs(effectiveFps);
        timer += Time.deltaTime;

        if (timer < frameDuration)
            return;

        int steps = Mathf.FloorToInt(timer / frameDuration);
        timer -= steps * frameDuration;

        int dir = effectiveFps > 0f ? 1 : -1;
        int frameCount = anim.frames.Count;
        int nextFrame = currentFrame + steps * dir;

        if (anim.loop)
        {
            currentFrame = ((nextFrame % frameCount) + frameCount) % frameCount;
            ApplyCurrentFrame();
        }
        else
        {
            if (dir > 0 && nextFrame >= frameCount)
            {
                currentFrame = frameCount - 1;
                isPlaying = false;
                timer = 0f;
                ApplyCurrentFrame();
                OnAnimationFinished?.Invoke(GetAnimationName(anim));
            }
            else if (dir < 0 && nextFrame < 0)
            {
                currentFrame = 0;
                isPlaying = false;
                timer = 0f;
                ApplyCurrentFrame();
                OnAnimationFinished?.Invoke(GetAnimationName(anim));
            }
            else
            {
                currentFrame = nextFrame;
                ApplyCurrentFrame();
            }
        }
    }

    /// <summary>Switch to an animation by name (or sprite sheet name if the animation name was left blank).</summary>
    public void Play(string animationName, bool restartIfSame = false)
    {
        int index = FindAnimationIndex(animationName);
        if (index < 0)
        {
            Debug.LogWarning($"[AnimationController] Animation '{animationName}' not found on '{gameObject.name}'.", this);
            return;
        }

        Play(index, restartIfSame);
    }

    /// <summary>Switch to an animation by its index in the Animations list.</summary>
    public void Play(int index, bool restartIfSame = false)
    {
        if (index < 0 || index >= animations.Count)
        {
            Debug.LogWarning($"[AnimationController] Animation index {index} is out of range on '{gameObject.name}'.", this);
            return;
        }

        // Prevents resetting to frame 0 if called repeatedly every Update() while already playing
        if (currentAnimIndex == index && isPlaying && !restartIfSame)
            return;

        CacheRenderer();
        currentAnimIndex = index;
        currentFrame = 0;
        timer = 0f;
        isPlaying = true;

        ApplyCurrentFrame();
    }

    public void Pause()
    {
        isPlaying = false;
    }

    public void Resume()
    {
        if (currentAnimIndex >= 0 && currentAnimIndex < animations.Count)
            isPlaying = true;
    }

    public void Stop()
    {
        isPlaying = false;
        timer = 0f;
        currentFrame = 0;
        ApplyCurrentFrame();
    }

    public void Restart()
    {
        if (currentAnimIndex >= 0 && currentAnimIndex < animations.Count)
            Play(currentAnimIndex, restartIfSame: true);
    }

    /// <summary>Sets the global speed multiplier (1 = normal, 0.5 = half speed, 2 = double speed).</summary>
    public void SetSpeed(float multiplier)
    {
        speedMultiplier = multiplier;
    }

    /// <summary>Changes the FPS of a specific animation at runtime.</summary>
    public void SetAnimationFPS(string animationName, float fps)
    {
        int index = FindAnimationIndex(animationName);
        if (index >= 0)
            animations[index].framesPerSecond = fps;
    }

    public bool HasAnimation(string animationName)
    {
        return FindAnimationIndex(animationName) >= 0;
    }

    private int FindAnimationIndex(string animationName)
    {
        if (string.IsNullOrWhiteSpace(animationName))
            return -1;

        for (int i = 0; i < animations.Count; i++)
        {
            if (animations[i] == null) continue;
            string animName = GetAnimationName(animations[i]);
            if (string.Equals(animName, animationName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string GetAnimationName(SpriteAnimation anim)
    {
        if (!string.IsNullOrWhiteSpace(anim.name))
            return anim.name;

        if (anim.spriteSheets != null && anim.spriteSheets.Count > 0 && anim.spriteSheets[0] != null)
            return anim.spriteSheets[0].name;

        return string.Empty;
    }

    private void CacheRenderer()
    {
        if (targetRenderer != null)
            return;

        targetRenderer = GetComponent<SpriteRenderer>();
        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<SpriteRenderer>();
        if (targetRenderer == null)
            targetRenderer = gameObject.AddComponent<SpriteRenderer>();
    }

    private void ApplyCurrentFrame()
    {
        if (targetRenderer == null || currentAnimIndex < 0 || currentAnimIndex >= animations.Count)
            return;

        List<Sprite> frames = animations[currentAnimIndex].frames;
        if (frames == null || frames.Count == 0)
            return;

        currentFrame = Mathf.Clamp(currentFrame, 0, frames.Count - 1);
        targetRenderer.sprite = frames[currentFrame];
    }

#if UNITY_EDITOR
    [ContextMenu("Reload Sprite Sheets")]
    private void OnValidate()
    {
        if (animations == null)
            return;

        foreach (SpriteAnimation anim in animations)
        {
            if (anim == null || anim.spriteSheets == null)
                continue;

            bool hasAnySheet = false;
            foreach (Texture2D sheet in anim.spriteSheets)
            {
                if (sheet != null)
                {
                    hasAnySheet = true;
                    break;
                }
            }

            if (!hasAnySheet)
                continue;

            List<Sprite> extractedFrames = new List<Sprite>();

            foreach (Texture2D sheet in anim.spriteSheets)
            {
                if (sheet == null) continue;

                string path = UnityEditor.AssetDatabase.GetAssetPath(sheet);
                if (string.IsNullOrEmpty(path)) continue;

                UnityEngine.Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
                List<Sprite> sheetSprites = new List<Sprite>();

                foreach (UnityEngine.Object asset in assets)
                {
                    if (asset is Sprite sprite)
                        sheetSprites.Add(sprite);
                }

                if (sheetSprites.Count == 0)
                {
                    Debug.LogWarning(
                        $"[AnimationController] No Sprites found in '{sheet.name}' on '{gameObject.name}'. " +
                        "Select the image in Unity, set Texture Type to 'Sprite (2D and UI)', " +
                        "Sprite Mode to 'Multiple', and slice it in the Sprite Editor.",
                        this
                    );
                    continue;
                }

                sheetSprites.Sort((a, b) => NaturalCompare(a.name, b.name));
                extractedFrames.AddRange(sheetSprites);
            }

            if (!AreFrameListsEqual(anim.frames, extractedFrames))
                anim.frames = extractedFrames;
        }
    }

    private static bool AreFrameListsEqual(List<Sprite> a, List<Sprite> b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    // Sorts frame names naturally so "walk_2" comes before "walk_10"
    private static int NaturalCompare(string a, string b)
    {
        if (a == b) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int startA = i;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                int startB = j;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                string numA = a.Substring(startA, i - startA).TrimStart('0');
                string numB = b.Substring(startB, j - startB).TrimStart('0');

                if (numA.Length != numB.Length)
                    return numA.Length.CompareTo(numB.Length);

                int cmp = string.CompareOrdinal(numA, numB);
                if (cmp != 0)
                    return cmp;

                int lenA = i - startA;
                int lenB = j - startB;
                if (lenA != lenB)
                    return lenA.CompareTo(lenB);
            }
            else
            {
                int cmp = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (cmp != 0)
                    return cmp;
                i++;
                j++;
            }
        }

        return a.Length.CompareTo(b.Length);
    }
#endif
}