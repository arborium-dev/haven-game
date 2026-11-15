using UnityEngine;


/// Player options that carry between scenes and play sessions (saved with PlayerPrefs).
public static class game_settings
{
    private const string CutscenesKey = "cutscenes_enabled";
    private const string TutorialKey = "tutorial_enabled";
    private const string Player2BotKey = "player2_bot";
    private const string SplitMusicKey = "split_music";
    private const string HardModeKey = "hard_mode";

    /// <summary>When on, specials speed the ball up more (see special_ability's Hard Mode Extra Speed).</summary>
    public static bool HardMode
    {
        get => PlayerPrefs.GetInt(HardModeKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(HardModeKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// OFF: one song for the whole game (the music plays as set up in the scene).
    /// ON: separate tracks for the tutorial, the main game, and after a special has been used (see music_manager).
    /// </summary>
    public static bool SplitMusic
    {
        get => PlayerPrefs.GetInt(SplitMusicKey, 1) == 1; // split music is the default
        set
        {
            PlayerPrefs.SetInt(SplitMusicKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

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
