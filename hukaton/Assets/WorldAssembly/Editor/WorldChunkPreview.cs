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
            // "seed radius swamp": round the heart of the nearest rotten swamp instead of the start.
            int cx = 0, cy = 0;
            bool swamp = parts.Length > 2 && parts[2] == "swamp";
            if (swamp)
            {
                int best = -1;
                for (int x = -14; x <= 14; x++)
                    for (int y = -14; y <= 14; y++)
                    {
                        if (!map.Biomes.IsSwamp(new ChunkCoord(x, y))) continue;
                        int around = 0;
                        for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++) if (map.Biomes.IsSwamp(new ChunkCoord(x + i, y + j))) around++;
                        int score = around * 100 - (x * x + y * y);
                        if (score > best) { best = score; cx = x; cy = y; }
                    }
            }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Preview world");
            SceneManager.MoveGameObjectToScene(root, scene);
            int missing = 0;
            for (int x = cx - radius; x <= cx + radius; x++)
                for (int y = cy - radius; y <= cy + radius; y++)
                {
                    var plan = map.Chunk(x, y);
                    var prefab = catalogue.Find(plan.Shape.Id, plan.Variant.Id, plan.Biome == Biome.Swamp ? ChunkCatalog.Swamp : "");
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

            if (swamp)
            {
                // Inside the swamp, from eye height and from a little above, the four ways round.
                var c = new Vector3(cx * 16, 0, cy * 16);
                var physics = scene.GetPhysicsScene();
                float Ground(Vector3 at) => physics.Raycast(at + Vector3.up * 60, Vector3.down, out var hit, 120) ? hit.point.y : 0;
                cam.fieldOfView = 70;
                for (int k = 0; k < 4; k++)
                {
                    var from = c + Quaternion.Euler(0, k * 90, 0) * new Vector3(0, 0, -10);
                    cam.transform.SetPositionAndRotation(from + Vector3.up * (Ground(from) + 1.7f), Quaternion.Euler(8, k * 90, 0));
                    Shot(cam, Path.Combine(dir, $"swamp-{seed}-eye-{k}.png"), 1600, 900);
                }
                cam.transform.SetPositionAndRotation(c + new Vector3(-22, 18, -22), Quaternion.Euler(30, 45, 0));
                Shot(cam, Path.Combine(dir, $"swamp-{seed}-above.png"), 1600, 900);
                cam.orthographic = true;
                cam.orthographicSize = (radius + .5f) * 16;
                cam.transform.SetPositionAndRotation(c + new Vector3(0, 200, 0), Quaternion.Euler(90, 0, 0));
                Shot(cam, Path.Combine(dir, $"swamp-{seed}-top.png"), 1400, 1400);
                EditorSceneManager.ClosePreviewScene(scene);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                return $"photographed the swamp of seed {seed} round chunk ({cx}, {cy}), radius {radius}, missing prefabs {missing}";
            }

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
