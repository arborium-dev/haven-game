using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// Menu screen: a "Cutscenes" checkbox (saved between sessions) and a Start button that plays the
/// wave transition into the game.
public class main_menu : MonoBehaviour
{
    [Header("Your UI (optional - built automatically if empty)")]
    [SerializeField] private Button startButton;
    [SerializeField] private Toggle cutscenesToggle;
    [Tooltip("Empty = a copy of your Cutscenes toggle is placed underneath it, labelled Tutorial Label.")]
    [SerializeField] private Toggle tutorialToggle;
    [Tooltip("Ticked = Player 2 is a bot. Empty = a copy of your Tutorial toggle is placed underneath it.")]
    [SerializeField] private Toggle player2BotToggle;
    [Tooltip("Ticked = separate tutorial / main / post-special music. Empty = a copy of the P2 Bot toggle underneath it.")]
    [SerializeField] private Toggle splitMusicToggle;
    [Tooltip("Ticked = hard mode (specials speed the ball up more). Empty = a copy of the toggle above, underneath it.")]
    [SerializeField] private Toggle hardModeToggle;

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "Gameplay";

    [Header("Auto-built UI")]
    [Tooltip("Font for the auto-built button/checkbox. Empty = same font as the first text in the scene (your title).")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private string startLabel = "START";
    [SerializeField] private string cutscenesLabel = "CUTSCENES";
    [SerializeField] private string tutorialLabel = "TUTORIAL";
    [SerializeField] private string player2BotLabel = "P2 BOT";
    [SerializeField] private string splitMusicLabel = "SPLIT MUSIC";
    [SerializeField] private string hardModeLabel = "HARD MODE";
    [Tooltip("Gap between the Cutscenes toggle and the Tutorial toggle under it.")]
    [SerializeField] private float toggleSpacing = 20f;
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
        if (cutscenesToggle == null && canvas != null) cutscenesToggle = BuildToggle(canvas.transform, cutscenesLabel, togglePosition);
        if (tutorialToggle == null && cutscenesToggle != null) tutorialToggle = CopyToggleBelow(cutscenesToggle, tutorialLabel);
        if (player2BotToggle == null)
        {
            Toggle above = tutorialToggle != null ? tutorialToggle : cutscenesToggle;
            if (above != null) player2BotToggle = CopyToggleBelow(above, player2BotLabel);
        }
        if (splitMusicToggle == null)
        {
            Toggle above = player2BotToggle != null ? player2BotToggle : (tutorialToggle != null ? tutorialToggle : cutscenesToggle);
            if (above != null) splitMusicToggle = CopyToggleBelow(above, splitMusicLabel);
        }
        if (hardModeToggle == null)
        {
            Toggle above = splitMusicToggle != null ? splitMusicToggle
                         : player2BotToggle != null ? player2BotToggle
                         : tutorialToggle != null ? tutorialToggle : cutscenesToggle;
            if (above != null) hardModeToggle = CopyToggleBelow(above, hardModeLabel);
        }

        if (cutscenesToggle != null)
        {
            cutscenesToggle.SetIsOnWithoutNotify(game_settings.CutscenesEnabled);
            cutscenesToggle.onValueChanged.AddListener(on => game_settings.CutscenesEnabled = on);
        }

        if (tutorialToggle != null)
        {
            tutorialToggle.SetIsOnWithoutNotify(game_settings.TutorialEnabled);
            tutorialToggle.onValueChanged.AddListener(on => game_settings.TutorialEnabled = on);
        }

        if (player2BotToggle != null)
        {
            player2BotToggle.SetIsOnWithoutNotify(game_settings.Player2IsBot);
            player2BotToggle.onValueChanged.AddListener(on => game_settings.Player2IsBot = on);
        }

        if (splitMusicToggle != null)
        {
            splitMusicToggle.SetIsOnWithoutNotify(game_settings.SplitMusic);
            splitMusicToggle.onValueChanged.AddListener(on => game_settings.SplitMusic = on);
        }

        if (hardModeToggle != null)
        {
            hardModeToggle.SetIsOnWithoutNotify(game_settings.HardMode);
            hardModeToggle.onValueChanged.AddListener(on => game_settings.HardMode = on);
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(StartGame);
            // Keyboard / controller: Start is selected, so Enter or Space starts the game.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(startButton.gameObject);
        }
    }

    private bool starting;

    private void Update()
    {
        if (starting) return;

        // Never leave a checkbox selected: otherwise Space / Enter would flip it instead of starting the game.
        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<Toggle>() != null)
            es.SetSelectedGameObject(startButton != null ? startButton.gameObject : null);

        // Space or Enter: start a new round with the settings as they are.
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            StartGame();
    }

    public void StartGame()
    {
        if (starting) return; // (the Start button and the key can both fire on the same press)
        if (cutscene_transition.Instance.IsChangingScene) return; // still revealing the menu: ignore until it's done
        starting = true;

        if (startButton != null) startButton.interactable = false;
        if (cutscenesToggle != null) cutscenesToggle.interactable = false;
        if (tutorialToggle != null) tutorialToggle.interactable = false;
        if (player2BotToggle != null) player2BotToggle.interactable = false;
        if (splitMusicToggle != null) splitMusicToggle.interactable = false;
        if (hardModeToggle != null) hardModeToggle.interactable = false;

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

    /// 
    /// Duplicates <paramref name="original"/> just underneath it with a new label, so the new toggle
    /// matches however you styled the original.
    /// 
    private Toggle CopyToggleBelow(Toggle original, string label)
    {
        GameObject copyObj = Instantiate(original.gameObject, original.transform.parent);
        copyObj.name = label + " Toggle";

        var rt = (RectTransform)copyObj.transform;
        var originalRt = (RectTransform)original.transform;
        rt.anchoredPosition = originalRt.anchoredPosition - new Vector2(0f, originalRt.rect.height + toggleSpacing);

        Toggle copy = copyObj.GetComponent<Toggle>();
        copy.onValueChanged = new Toggle.ToggleEvent(); // don't inherit anything wired to the original
        copy.group = null;

        TMP_Text text = copyObj.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;
        else
        {
            Text legacy = copyObj.GetComponentInChildren<Text>(true);
            if (legacy != null) legacy.text = label;
        }
        return copy;
    }

    private Toggle BuildToggle(Transform parent, string label, Vector2 position)
    {
        float box = fontSize * 0.9f;

        var obj = new GameObject(label + " Toggle", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var rt = (RectTransform)obj.transform;
        rt.anchoredPosition = position;
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
        TMP_Text labelText = MakeLabel(obj.transform, label, TextAlignmentOptions.Left, Vector2.zero, Vector2.one);
        labelText.rectTransform.offsetMin = new Vector2(box + 16f, 0f);
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
