using UnityEditor;

namespace Hakaton.Dev
{
    /// <summary>
    /// Scripts change while the game is playing (agents commit in parallel). Unity's default "Recompile And Continue
    /// Playing" reloads the domain mid-session and drops every non-serialized object (TradingSession, deck model...),
    /// which breaks the running game. Force "Recompile After Finished Playing" for this project.
    /// </summary>
    [InitializeOnLoad]
    static class RecompileAfterPlay
    {
        const string Key = "ScriptCompilationDuringPlay";
        const int RecompileAfterFinishedPlaying = 1;

        static RecompileAfterPlay()
        {
            if (EditorPrefs.GetInt(Key, 0) != RecompileAfterFinishedPlaying)
                EditorPrefs.SetInt(Key, RecompileAfterFinishedPlaying);
        }
    }
}
