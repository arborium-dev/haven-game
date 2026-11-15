using UnityEngine;

/// <summary>
/// Player options that carry between scenes and play sessions (saved with PlayerPrefs).
/// </summary>
public static class game_settings
{
    private const string CutscenesKey = "cutscenes_enabled";

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
