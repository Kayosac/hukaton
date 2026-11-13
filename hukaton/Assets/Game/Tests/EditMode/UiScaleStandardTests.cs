using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hakaton.Feel;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The UI must look the same at any resolution, laid out for Full HD: every screen-space root canvas of the game
    /// scales with the window (Scale With Screen Size, 1920 × 1080, Expand). The exception is a canvas a camera
    /// renders into a texture (the PC screen), whose size is the texture's, not the window's.
    /// </summary>
    public sealed class UiScaleStandardTests
    {
        static IEnumerable<string> OwnAssets(string filter) => AssetDatabase.FindAssets(filter, new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !p.StartsWith("Assets/Plugins/") && !p.StartsWith("Assets/_Recovery/") && !p.Contains("/Febucci/"));

        static bool RendersToTexture(Canvas canvas)
            => canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera && canvas.worldCamera.targetTexture;

        static IEnumerable<string> Offenders(GameObject root, string where)
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas && canvas.transform.parent && canvas.transform.parent.GetComponentInParent<Canvas>(true)) continue;
                if (canvas.renderMode == RenderMode.WorldSpace || RendersToTexture(canvas)) continue;
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.name == "PC screen canvas") continue;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (!UiReference.Follows(scaler))
                    yield return $"{where}: {canvas.name} ({(scaler ? scaler.uiScaleMode.ToString() : "no scaler")})";
            }
        }

        [Test]
        public void EveryScreenCanvasPrefabScalesFromFullHd()
        {
            var bad = new List<string>();
            foreach (var path in OwnAssets("t:Prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab) bad.AddRange(Offenders(prefab, path));
            }
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        }

        [Test]
        public void EveryScreenCanvasInBuildScenesScalesFromFullHd()
        {
            var bad = new List<string>();
            var previous = SceneManager.GetActiveScene();
            foreach (var path in EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path))
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects()) bad.AddRange(Offenders(root, path));
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        }

        [TestCase(1920, 1080, 1f)]
        [TestCase(3840, 2160, 2f)]
        [TestCase(1280, 720, 2f / 3f)]
        [TestCase(3440, 1440, 4f / 3f)]
        [TestCase(1600, 1200, 1600f / 1920f)]
        public void ScaleFollowsFullHd(int width, int height, float expected)
            => Assert.That(UiReference.ScaleFor(width, height), Is.EqualTo(expected).Within(1e-4f));
    }
}
