using System.IO;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Lays the real chunk prefabs of a world out around the start in a throwaway preview scene and photographs it
    /// (from above and from the overlook) into Logs/world-plans. Nothing is saved.
    /// </summary>
    public static class WorldChunkPreview
    {
        [MenuItem("Hakaton/World/Photograph world (seed 2)")]
        static void Menu() => Debug.Log(Photograph("2"));

        public static string Photograph(string args)
        {
            var parts = args.Split(' ');
            int seed = int.Parse(parts[0]);
            int radius = parts.Length > 1 ? int.Parse(parts[1]) : 5;
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            var map = new WorldMap(seed);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Preview world");
            SceneManager.MoveGameObjectToScene(root, scene);
            int missing = 0;
            for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                {
                    var plan = map.Chunk(x, y);
                    var prefab = catalogue.Find(plan.Shape.Id, plan.Variant.Id);
                    if (!prefab) { missing++; continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                    go.transform.SetParent(root.transform, false);
                    go.transform.SetPositionAndRotation(new Vector3(x * 16, plan.BaseLevel * map.Rules.PlateauHeight, y * 16), Quaternion.Euler(0, 90 * plan.Rotation, 0));
                }
            var sun = new GameObject("Sun").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            var camGo = new GameObject("Camera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.55f, .6f, .62f);
            cam.farClipPlane = 1000;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);

            cam.orthographic = true;
            cam.orthographicSize = (radius + .5f) * 16;
            cam.transform.SetPositionAndRotation(new Vector3(0, 200, 0), Quaternion.Euler(90, 0, 0));
            Shot(cam, Path.Combine(dir, $"world-{seed}-top.png"), 1400, 1400);

            cam.orthographic = false;
            cam.fieldOfView = 60;
            cam.transform.SetPositionAndRotation(new Vector3(0, 6, -2), Quaternion.Euler(14, 0, 0));
            Shot(cam, Path.Combine(dir, $"world-{seed}-overlook-north.png"), 1600, 900);
            cam.transform.SetPositionAndRotation(new Vector3(0, 6, -2), Quaternion.Euler(14, 135, 0));
            Shot(cam, Path.Combine(dir, $"world-{seed}-overlook-southeast.png"), 1600, 900);
            cam.transform.SetPositionAndRotation(new Vector3(-30, 30, -60), Quaternion.Euler(28, 30, 0));
            Shot(cam, Path.Combine(dir, $"world-{seed}-bird.png"), 1600, 900);

            EditorSceneManager.ClosePreviewScene(scene);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            return $"photographed seed {seed}, radius {radius}, missing prefabs {missing}";
        }

        static void Shot(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.aspect = w / (float)h;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
