using System.IO;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// A contact sheet of one shape's variants (Logs/world-plans/scenes-&lt;shape&gt;.png): each chunk prefab alone
    /// on a flat apron, seen from a corner at a little above head height. Nothing is saved.
    /// </summary>
    public static class ScenePreview
    {
        const int W = 560, H = 380, Columns = 4;

        /// <summary>Every landmark laid out whole, each from two corners: Logs/world-plans/landmarks.png.</summary>
        public static string Landmarks(string only)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            var wanted = string.IsNullOrWhiteSpace(only) ? null : only.Split(',');
            var kinds = new System.Collections.Generic.List<LandmarkKind>();
            foreach (var k in WorldAssembly.Landmarks.All) if (wanted == null || System.Array.IndexOf(wanted, k.Id) >= 0) kinds.Add(k);
            const int views = 4;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var sheet = new Texture2D(W * views, H * kinds.Count, TextureFormat.RGB24, false);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            var sun = new GameObject("Sun").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            var camGo = new GameObject("Camera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.55f, .66f, .66f);
            cam.fieldOfView = 55;
            cam.farClipPlane = 500;
            var rt = new RenderTexture(W, H, 24);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            for (int k = 0; k < kinds.Count; k++)
            {
                var kind = kinds[k];
                var at = new Vector3(k * 200, 0, 0);
                var root = new GameObject(kind.Id);
                SceneManager.MoveGameObjectToScene(root, scene);
                for (int i = 0; i < kind.Size; i++)
                    for (int j = 0; j < kind.Size; j++)
                    {
                        var prefab = catalogue.Find(kind.PartId(i, j), "part");
                        if (!prefab) continue;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = at + new Vector3(i * 16 + 8, 0, j * 16 + 8);
                        Simulate(go);
                    }
                float size = kind.Size * 16;
                var centre = at + new Vector3(size / 2, 0, size / 2);
                var eyes = new[] { centre + new Vector3(-size * .75f, size * .55f, -size * .75f), centre + new Vector3(size * .8f, size * .3f, -size * .2f), centre + new Vector3(0, size * 1.25f, -size * .12f), centre + new Vector3(size * .1f, size * .22f, -size * .95f) };
                for (int v = 0; v < views; v++)
                {
                    cam.transform.position = eyes[v];
                    cam.transform.LookAt(centre + Vector3.up * 2);
                    cam.targetTexture = rt;
                    cam.aspect = W / (float)H;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    sheet.SetPixels(v * W, (kinds.Count - 1 - k) * H, W, H, tex.GetPixels());
                }
                Object.DestroyImmediate(root);
            }
            sheet.Apply();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, wanted == null ? "landmarks.png" : "landmarks-" + only.Replace(',', '-') + ".png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            RenderSettings.fog = fog;
            return $"sheet of {kinds.Count} landmarks";
        }

        /// <summary>Effects (waterfalls, smoke, flies) only show once their particles have run for a while.</summary>
        static void Simulate(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(3, true, true);
        }

        /// <summary>Heights of a landmark's ground along a line: "mesa x0 y0 x1 y1 [steps]" (scene coordinates).</summary>
        public static string Probe(string arg)
        {
            var a = arg.Split(' ');
            var kind = WorldAssembly.Landmarks.Get(a[0]);
            if (a[1] == "circle")
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                float cx = float.Parse(a[2], inv), cy = float.Parse(a[3], inv), r = float.Parse(a[4], inv), from = float.Parse(a[5], inv), to = float.Parse(a[6], inv);
                var text = new System.Text.StringBuilder();
                for (float deg = from; deg <= to + .01f; deg += 15)
                {
                    var q = new Vector2(cx + r * Mathf.Cos(deg * Mathf.Deg2Rad), cy + r * Mathf.Sin(deg * Mathf.Deg2Rad));
                    int ci = Mathf.Clamp((int)(q.x / 16), 0, kind.Size - 1), cj = Mathf.Clamp((int)(q.y / 16), 0, kind.Size - 1);
                    var tt = new TileTerrain(TileShapes.Get(kind.PartId(ci, cj)), "part", 6, 5);
                    text.Append($"\n{deg:0}° ({q.x:0.0},{q.y:0.0}) height {tt.Height(q - tt.Offset):0.00}");
                }
                return text.ToString();
            }
            float x0 = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), y0 = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
            float x1 = float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture), y1 = float.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture);
            int steps = a.Length > 5 ? int.Parse(a[5]) : 12;
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k <= steps; k++)
            {
                // "circle cx cy r a0 a1": x0 = cx, y0 = cy, x1 = r, y1 = a0, steps = a1 (degrees, 15° apart).
                var p = a[1] == "circle"
                    ? Vector2.zero
                    : Vector2.Lerp(new Vector2(x0, y0), new Vector2(x1, y1), k / (float)steps);
                int i = Mathf.Clamp((int)(p.x / 16), 0, kind.Size - 1), j = Mathf.Clamp((int)(p.y / 16), 0, kind.Size - 1);
                var t = new TileTerrain(TileShapes.Get(kind.PartId(i, j)), "part", 6, 5);
                var local = p - t.Offset;
                sb.Append($"\n({p.x:0.0},{p.y:0.0}) relief {t.Scene.Relief(t, p):0.00} height {t.Height(local):0.00}");
            }
            return sb.ToString();
        }

        public static string Sheet(string shapeId)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            var shape = TileShapes.Get(shapeId);
            int count = shape.Variants.Length, rows = (count + Columns - 1) / Columns;
            var sheet = new Texture2D(W * Columns, H * rows, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(40, 44, 48, 255);
            sheet.SetPixels32(fill);

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            var sun = new GameObject("Sun").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            var camGo = new GameObject("Camera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.55f, .66f, .66f);
            cam.fieldOfView = 55;
            var rt = new RenderTexture(W, H, 24);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            for (int v = 0; v < count; v++)
            {
                var prefab = catalogue.Find(shapeId, shape.Variants[v].Id);
                if (!prefab) continue;
                var at = new Vector3(v * 60, 0, 0);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                go.transform.position = at;
                Simulate(go);
                cam.transform.position = at + new Vector3(-2, 8.5f, -14.5f);
                cam.transform.LookAt(at + new Vector3(0, 0, 1.5f));
                cam.targetTexture = rt;
                cam.aspect = W / (float)H;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                int col = v % Columns, row = rows - 1 - v / Columns;
                sheet.SetPixels(col * W, row * H, W, H, tex.GetPixels());
                Object.DestroyImmediate(go);
            }
            sheet.Apply();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"scenes-{shapeId}.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            return $"sheet of {count} {shapeId} variants";
        }
    }
}
