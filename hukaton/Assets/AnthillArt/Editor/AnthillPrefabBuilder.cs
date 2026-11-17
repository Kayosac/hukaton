// ONE-OFF authoring tool (AGENTS.md: run once, verify, then delete this file and its .meta).
// Builds Assets/AnthillArt/Prefabs/<Group>/AN<NN>_<Name>.prefab from the imported anthill FBX:
// unpacked mesh children, shared M_Ant_* materials, MeshColliders on what is visibly solid,
// small point lights on the glowing mushrooms.
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Dev
{
    static class AnthillPrefabBuilder
    {
        const string Root = "Assets/AnthillArt";

        static string Build()
        {
            var log = new StringBuilder();
            var mats = new[] { "Earth", "Floor", "Root", "Wood", "Cloth", "Props" }
                .ToDictionary(k => k, k => AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/M_Ant_{k}.mat"));
            foreach (var kv in mats) if (kv.Value == null) return "FAILED missing material M_Ant_" + kv.Key;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string group = Path.GetFileName(Path.GetDirectoryName(path));
                string fbxName = Path.GetFileNameWithoutExtension(path);          // SM_AN01_EarthWall
                string prefabName = fbxName.Substring(3);                         // AN01_EarthWall
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = prefabName;
                go.transform.position = Vector3.zero;

                bool shellLike = group == "Shell" || group == "Entrance";
                bool chain = prefabName.StartsWith("AN30");
                int colliders = 0;
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string key = mats.Keys.FirstOrDefault(k => mr.name.EndsWith("_" + k));
                    // A one-mesh FBX imports as the root itself (named after the file): those are all Props-only assets.
                    if (key == null && mr.gameObject == go) key = "Props";
                    if (key == null) { log.AppendLine($"WARN {prefabName}: no material for {mr.name}"); continue; }
                    mr.sharedMaterials = Enumerable.Repeat(mats[key], mr.sharedMaterials.Length).ToArray();
                    // Hanging root tips / rootlets in walls and on the mound stay passable (like foliage); chain hangs free.
                    bool passable = chain || (shellLike && key == "Root");
                    if (!passable)
                    {
                        var mc = mr.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
                        colliders++;
                    }
                }
                AddGlow(go, prefabName);

                string dir = $"{Root}/Prefabs/{group}";
                Directory.CreateDirectory(dir);
                PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/{prefabName}.prefab");
                Object.DestroyImmediate(go);
                log.AppendLine($"{group}/{prefabName} colliders={colliders}");
            }
            AssetDatabase.SaveAssets();
            return log.ToString().Replace("\n", " | ");
        }

        static void AddGlow(GameObject go, string name)
        {
            // (prefab, colour, range, intensity, height)
            if (name.StartsWith("AN37")) Light(go, new Color(0.55f, 0.95f, 0.70f), 5f, 1.4f, 2.0f);
            else if (name.StartsWith("AN38")) Light(go, new Color(0.85f, 0.95f, 0.45f), 3f, 1.0f, 0.5f);
            else if (name.StartsWith("AN39")) Light(go, new Color(0.85f, 0.95f, 0.45f), 2.5f, 0.8f, 0.4f);
        }

        static void Light(GameObject go, Color color, float range, float intensity, float height)
        {
            var l = new GameObject("Glow Light").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = new Vector3(0, height, 0);
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }
        // Renders every anthill prefab in an isolated preview scene (no project scene is touched).
        static string Preview(string arg)
        {
            bool dark = arg == "dark";
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Prefabs" });
            var pru = new PreviewRenderUtility();
            try
            {
                pru.camera.fieldOfView = 30;
                pru.camera.nearClipPlane = 0.1f; pru.camera.farClipPlane = 400;
                pru.camera.clearFlags = CameraClearFlags.SolidColor;
                pru.camera.backgroundColor = dark ? new Color(0.02f, 0.015f, 0.012f) : new Color(0.55f, 0.53f, 0.5f);
                pru.lights[0].intensity = dark ? 0.08f : 1.1f; pru.lights[0].transform.rotation = Quaternion.Euler(45, 35, 0);
                pru.lights[1].intensity = dark ? 0.0f : 0.5f;
                pru.ambientColor = dark ? new Color(0.03f, 0.025f, 0.02f) : new Color(0.35f, 0.33f, 0.3f);
                int cols = 7; float cell = 8.5f; int i = 0;
                foreach (var g in guids.OrderBy(x => Path.GetFileName(AssetDatabase.GUIDToAssetPath(x))))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    var inst = pru.InstantiatePrefabInScene(prefab);
                    inst.transform.position = new Vector3((i % cols) * cell, 0, -(i / cols) * cell);
                    i++;
                }
                int rows = (i + cols - 1) / cols;
                var center = new Vector3((cols - 1) * cell / 2, 0, -(rows - 1) * cell / 2);
                pru.camera.transform.position = center + new Vector3(0, 62, -78);
                pru.camera.transform.LookAt(center + Vector3.up * 1.5f);
                pru.BeginStaticPreview(new Rect(0, 0, 2400, 1600));
                pru.Render(true);
                var tex = pru.EndStaticPreview();
                string file = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", dark ? "an_preview_dark.png" : "an_preview.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                return file + " prefabs=" + i;
            }
            finally { pru.Cleanup(); }
        }

        static string Probe(string arg)
        {
            var sb = new StringBuilder();
            foreach (var n in arg.Split(','))
            {
                var path = AssetDatabase.FindAssets(n + " t:Prefab", new[] { Root + "/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var b = new Bounds(); bool first = true;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; var bb = m.bounds;
                    var mat = mf.transform.localToWorldMatrix;
                    foreach (var v in m.vertices) { var w = mat.MultiplyPoint3x4(v); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
                }
                sb.Append(n).Append(" min=").Append(b.min.ToString("F2")).Append(" max=").Append(b.max.ToString("F2")).Append(" | ");
            }
            return sb.ToString();
        }
    }
}
