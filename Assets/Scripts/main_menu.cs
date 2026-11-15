using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Menu screen: a "Cutscenes" checkbox (saved between sessions) and a Start button that plays the
/// wave transition into the game.
///
/// Add this to the menu's Canvas. If Start Button / Cutscenes Toggle are empty, simple ones are built
/// automatically in your title's font. To use your own, make a Button and a Toggle and drag them in.
/// </summary>
public class main_menu : MonoBehaviour
{
    [Header("Your UI (optional - built automatically if empty)")]
    [SerializeField] private Button startButton;
    [SerializeField] private Toggle cutscenesToggle;

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "Gameplay";

    [Header("Auto-built UI")]
    [Tooltip("Font for the auto-built button/checkbox. Empty = same font as the first text in the scene (your title).")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private string startLabel = "START";
    [SerializeField] private string cutscenesLabel = "CUTSCENES";
    [SerializeField] private Vector2 startButtonPosition = new Vector2(0f, -120f);
    [SerializeField] private Vector2 startButtonSize = new Vector2(280f, 80f);
    [SerializeField] private Vector2 togglePosition = new Vector2(0f, -220f);
    [SerializeField] private float fontSize = 44f;
    [SerializeField] private Color buttonColor = new Color32(0x6B, 0x2C, 0x91, 0xFF);   // purple
    [SerializeField] private Color textColor = new Color32(0xF6, 0xE6, 0xC4, 0xFF);     // cream
    [SerializeField] private Color outlineColor = new Color32(0x2A, 0x10, 0x3A, 0xFF);  // dark purple
    [SerializeField] private Color checkColor = new Color32(0xEC, 0x27, 0x6E, 0xFF);    // pink

    private void Start()
    {
        Time.timeScale = 1f;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (font == null)
        {
            TMP_Text anyText = FindAnyObjectByType<TMP_Text>();
            if (anyText != null) font = anyText.font;
        }

        if (startButton == null && canvas != null) startButton = BuildButton(canvas.transform);
        if (cutscenesToggle == null && canvas != null) cutscenesToggle = BuildToggle(canvas.transform);

        if (cutscenesToggle != null)
        {
            cutscenesToggle.SetIsOnWithoutNotify(game_settings.CutscenesEnabled);
            cutscenesToggle.onValueChanged.AddListener(on => game_settings.CutscenesEnabled = on);
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(StartGame);
            // Keyboard / controller: Start is selected, so Enter or Space starts the game.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(startButton.gameObject);
        }
    }

    public void StartGame()
    {
        if (startButton != null) startButton.interactable = false;
        if (cutscenesToggle != null) cutscenesToggle.interactable = false;

        // The wave grows out from the Start button.
        Vector2 origin = new Vector2(0.5f, 0.5f);
        if (startButton != null)
        {
            Canvas c = startButton.GetComponentInParent<Canvas>();
            Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, startButton.transform.position);
            origin = new Vector2(screen.x / Screen.width, screen.y / Screen.height);
        }

        cutscene_transition.Instance.TransitionToScene(gameSceneName, origin);
    }

    // ---------- Auto-built UI ----------

    private Button BuildButton(Transform parent)
    {
        var obj = new GameObject("Start Button", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var rt = (RectTransform)obj.transform;
        rt.anchoredPosition = startButtonPosition;
        rt.sizeDelta = startButtonSize;

        var img = obj.AddComponent<Image>();
        img.color = buttonColor;
        AddPixelOutline(obj);

        var button = obj.AddComponent<Button>();
        button.targetGraphic = img;
        button.colors = HoverColors();

        MakeLabel(obj.transform, startLabel, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
        return button;
    }

    private Toggle BuildToggle(Transform parent)
    {
        float box = fontSize * 0.9f;

        var obj = new GameObject("Cutscenes Toggle", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var rt = (RectTransform)obj.transform;
        rt.anchoredPosition = togglePosition;
        rt.sizeDelta = new Vector2(startButtonSize.x + 80f, box);

        // Checkbox square on the left.
        var boxObj = new GameObject("Box", typeof(RectTransform));
        boxObj.transform.SetParent(obj.transform, false);
        var boxRt = (RectTransform)boxObj.transform;
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
        boxRt.pivot = new Vector2(0f, 0.5f);
        boxRt.sizeDelta = new Vector2(box, box);
        var boxImg = boxObj.AddComponent<Image>();
        boxImg.color = textColor;
        AddPixelOutline(boxObj);

        // The check mark: a smaller solid square inside the box.
        var checkObj = new GameObject("Check", typeof(RectTransform));
        checkObj.transform.SetParent(boxObj.transform, false);
        var checkRt = (RectTransform)checkObj.transform;
        checkRt.anchorMin = checkRt.anchorMax = new Vector2(0.5f, 0.5f);
        checkRt.sizeDelta = new Vector2(box * 0.6f, box * 0.6f);
        var checkImg = checkObj.AddComponent<Image>();
        checkImg.color = checkColor;

        var toggle = obj.AddComponent<Toggle>();
        toggle.targetGraphic = boxImg;
        toggle.graphic = checkImg;
        toggle.colors = HoverColors();

        // Label to the right (clicking it toggles too).
        TMP_Text label = MakeLabel(obj.transform, cutscenesLabel, TextAlignmentOptions.Left, Vector2.zero, Vector2.one);
        label.rectTransform.offsetMin = new Vector2(box + 16f, 0f);
        return toggle;
    }

    private TMP_Text MakeLabel(Transform parent, string text, TextAlignmentOptions align, Vector2 anchorMin, Vector2 anchorMax)
    {
        var obj = new GameObject("Label", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    private void AddPixelOutline(GameObject obj)
    {
        var outline = obj.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(4f, -4f);
    }

    private static ColorBlock HoverColors()
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.selectedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.pressedColor = new Color(0.65f, 0.65f, 0.65f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f);
        colors.fadeDuration = 0.05f;
        return colors;
    }
}
