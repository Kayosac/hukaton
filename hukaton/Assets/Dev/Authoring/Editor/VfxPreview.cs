using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.Authoring
{
    /// <summary>
    /// Renders particle prefabs at several moments into one contact sheet (rows = prefabs, columns = times).
    /// args: "out.png|distance|path1;path2;..." — paths relative to Assets/Plugins/Retro Arsenal/Prefabs/ unless they start with Assets/.
    /// Temporary authoring tool.
    /// </summary>
    static class VfxPreview
    {
        static readonly float[] Times = { .08f, .2f, .4f, .7f, 1.1f };
        const int Cell = 200;

        public static string Sheet(string args)
        {
            var parts = args.Split('|');
            string output = Path.Combine("Logs/bridge/shots", parts[0]);
            float distance = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var paths = parts[2].Split(';').Where(p => p.Length > 0)
                .Select(p => p.StartsWith("Assets/") ? p : "Assets/Plugins/Retro Arsenal/Prefabs/" + p).ToArray();

            var scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Preview camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.32f, .38f, .30f);
            camera.fieldOfView = 50;
            camera.transform.position = new Vector3(0, 1.2f, -distance);
            camera.transform.LookAt(new Vector3(0, .6f, 0));
            var lightObject = new GameObject("Preview light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);

            var target = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var sheet = new Texture2D(Cell * Times.Length, Cell * paths.Length, TextureFormat.RGB24, false);
            var report = new System.Text.StringBuilder();
            for (int row = 0; row < paths.Length; row++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[row]);
                report.AppendLine($"{row}: {paths[row]}{(prefab ? "" : "  (missing)")}");
                if (!prefab) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.position = Vector3.zero;
                foreach (var source in instance.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
                var systems = instance.GetComponentsInChildren<ParticleSystem>(true)
                    .Where(s => !s.transform.parent || !s.transform.parent.GetComponentInParent<ParticleSystem>()).ToArray();
                for (int column = 0; column < Times.Length; column++)
                {
                    foreach (var system in systems) system.Simulate(Times[column], true, true, false);
                    camera.Render();
                    RenderTexture.active = target;
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), column * Cell, (paths.Length - 1 - row) * Cell);
                    RenderTexture.active = null;
                }
                Object.DestroyImmediate(instance);
            }
            sheet.Apply();
            File.WriteAllBytes(output, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(output + ".txt", report.ToString());
            return report.ToString().Replace("\n", " | ");
        }
    }
}
