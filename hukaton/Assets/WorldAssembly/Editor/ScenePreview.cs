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
