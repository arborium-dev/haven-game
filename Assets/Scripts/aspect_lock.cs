using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the whole game at exactly 16:9 on any window or monitor:
///   - The main camera gets a centered 16:9 viewport. Anything left over becomes black bars
///     (top/bottom on taller screens, left/right on wider ones), so nobody ever sees more or less of the table.
///   - Screen UI (the score, menu buttons...) is moved onto that camera, so it stays inside the 16:9 area
///     instead of drifting into the bars or off the edge.
///
/// Starts itself automatically and lives across scenes. Nothing to add to any scene.
/// </summary>
public class aspect_lock : MonoBehaviour
{
    public const float TargetAspect = 16f / 9f;

    /// <summary>Screen UI moved onto the camera gets this added to its draw order, so it stays above the game.</summary>
    public const int UiSortingBase = 1000;

    private static aspect_lock instance;

    private Camera barsCamera;
    private Camera lastMainCamera;
    private int lastWidth, lastHeight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var obj = new GameObject("aspect_lock");
        DontDestroyOnLoad(obj);
        instance = obj.AddComponent<aspect_lock>();
    }

    private void Awake()
    {
        BuildBarsCamera();
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Apply(moveUI: true);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this) instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Apply(moveUI: true);

    private void LateUpdate()
    {
        // Re-fit if the window is resized / goes fullscreen, or the main camera changes.
        if (Screen.width != lastWidth || Screen.height != lastHeight || Camera.main != lastMainCamera)
            Apply(moveUI: Camera.main != lastMainCamera);
    }

    private void Apply(bool moveUI)
    {
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        Camera cam = Camera.main;
        lastMainCamera = cam;
        if (cam == null) return;

        cam.rect = ViewportRect();
        if (moveUI) MoveScreenUIOntoCamera(cam);
    }

    /// <summary>The centered 16:9 part of the screen, as a 0-1 viewport rect.</summary>
    public static Rect ViewportRect()
    {
        float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        if (screenAspect > TargetAspect)
        {
            float w = TargetAspect / screenAspect; // wider screen: bars on the left and right
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        float h = screenAspect / TargetAspect;     // taller screen: bars on the top and bottom
        return new Rect(0f, (1f - h) * 0.5f, 1f, h);
    }

    /// <summary>A camera behind everything that just paints the whole screen black (the bars).</summary>
    private void BuildBarsCamera()
    {
        var obj = new GameObject("LetterboxBars");
        obj.transform.SetParent(transform, false);
        barsCamera = obj.AddComponent<Camera>();
        barsCamera.depth = -100f;          // draws first, the game camera draws over it
        barsCamera.cullingMask = 0;        // renders nothing but the clear color
        barsCamera.clearFlags = CameraClearFlags.SolidColor;
        barsCamera.backgroundColor = Color.black;
        barsCamera.orthographic = true;
    }

    /// <summary>
    /// Screen-overlay canvases always cover the whole screen, bars included. Putting them on the camera instead
    /// keeps them inside the 16:9 area. (The cutscene wave transition is left alone; it's meant to cover everything.)
    /// </summary>
    private static void MoveScreenUIOntoCamera(Camera cam)
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>())
        {
            if (canvas == null || !canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (canvas.GetComponentInParent<cutscene_transition>() != null) continue;
            AttachToCamera(canvas, cam, canvas.sortingOrder);
        }
    }

    /// <summary>Puts a canvas on the game camera (inside the 16:9 area), drawn above the game.</summary>
    public static void AttachToCamera(Canvas canvas, Camera cam, int order)
    {
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.05f, 1f);
        canvas.sortingOrder = UiSortingBase + order;
    }
}
