using UnityEngine;

/// <summary>
/// Player options that carry between scenes and play sessions (saved with PlayerPrefs).
/// </summary>
public static class game_settings
{
    private const string CutscenesKey = "cutscenes_enabled";
    private const string TutorialKey = "tutorial_enabled";

    /// <summary>When on, each game starts with the tutorial (for every human player).</summary>
    public static bool TutorialEnabled
    {
        get => PlayerPrefs.GetInt(TutorialKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>When off, specials skip their cutscene (and its wave transition).</summary>
    public static bool CutscenesEnabled
    {
        get => PlayerPrefs.GetInt(CutscenesKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(CutscenesKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
