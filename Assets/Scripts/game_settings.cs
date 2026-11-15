using UnityEngine;


/// Player options that carry between scenes and play sessions (saved with PlayerPrefs).
public static class game_settings
{
    private const string CutscenesKey = "cutscenes_enabled";
    private const string TutorialKey = "tutorial_enabled";
    private const string Player2BotKey = "player2_bot";

    /// When on, Player 2 is played by the bot. When off, Player 2 is a second human (arrow keys + Enter).
    public static bool Player2IsBot
    {
        get => PlayerPrefs.GetInt(Player2BotKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(Player2BotKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// When on, each game starts with the tutorial (for every human player).
    public static bool TutorialEnabled
    {
        get => PlayerPrefs.GetInt(TutorialKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// When off, specials skip their cutscene (and its wave transition).
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
