using Hakaton.Teaser;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hakaton.Teaser.EditorTools
{
    /// <summary>One click re-render of the PENUS teaser: opens its scene, sets the Game view to 1920 x 1080 and plays
    /// one recorded run. Frames land in Logs/teaser-frames (see Assets/Teaser/README.md for the MP4).</summary>
    static class TeaserRenderMenu
    {
        const string ScenePath = "Assets/Teaser/Scenes/PenusTeaser.unity";

        [MenuItem("Hakaton/Teaser/Render frames (1920x1080)")]
        static void Render()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath);
            }
            var playModeWindow = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeWindow");
            var setResolution = playModeWindow?.GetMethod("SetCustomRenderingResolution",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (setResolution != null) setResolution.Invoke(null, new object[] { 1920u, 1080u, "PENUS teaser 1080p" });
            else Debug.LogWarning("Set the Game view to 1920 x 1080 by hand before rendering the teaser.");
            var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"), false, null, false);
            gameView.Show();
            gameView.Focus();
            SessionState.SetBool(TeaserRecorder.RecordOnceKey, true);
            EditorApplication.isPlaying = true;
        }
    }
}
