using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using Hakaton.WorldAssembly.Unity;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// How heavy the art is: triangles, renderers and materials per prefab (Assets/ForestArt, Assets/SwampArt) and per
    /// built chunk (the forest's and the swamp's), written to Logs/world-plans/art-stats.txt.
    /// </summary>
    public static class ArtStats
    {
        public static string Run(string arg)
        {
            var sb = new StringBuilder();
            foreach (var root in new[] { "Assets/ForestArt/Prefabs", "Assets/SwampArt/Prefabs" })
            {
                var rows = new List<(string name, int tris, int renderers, int mats)>();
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var (tris, renderers, mats) = Measure(go);
                    rows.Add((path.Substring(root.Length + 1), tris, renderers, mats));
                }
                sb.AppendLine($"== {root}: {rows.Count} prefabs, mean tris {rows.Average(r => r.tris):0}, mean renderers {rows.Average(r => r.renderers):0.0}");
                foreach (var r in rows.OrderByDescending(r => r.tris)) sb.AppendLine($"{r.tris,8} tris {r.renderers,3} rend {r.mats,3} mats  {r.name}");
            }
            sb.AppendLine("== chunks");
            var chunks = new List<(string name, bool swamp, int tris, int renderers, int mats, int shadow)>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/WorldAssembly/Chunks" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool swamp = path.Contains("swamp") || TileShapes.IsSwampScene(System.IO.Path.GetFileNameWithoutExtension(path).Split(' ').Last());
                var (tris, renderers, mats) = Measure(go);
                int shadow = go.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
                chunks.Add((System.IO.Path.GetFileNameWithoutExtension(path), swamp, tris, renderers, mats, shadow));
            }
            foreach (var group in chunks.GroupBy(c => c.swamp))
                sb.AppendLine($"{(group.Key ? "swamp" : "forest")}: {group.Count()} chunks, mean tris {group.Average(c => c.tris):0}, max {group.Max(c => c.tris)}, mean renderers {group.Average(c => c.renderers):0}, mean shadow casters {group.Average(c => c.shadow):0}");
            foreach (var c in chunks.OrderByDescending(c => c.tris).Take(40)) sb.AppendLine($"{c.tris,8} tris {c.renderers,4} rend {c.shadow,4} shadow  {(c.swamp ? "S" : "F")} {c.name}");
            // Which renderers could merge: by the components on them and above them up to the chunk.
            var kinds = new Dictionary<string, int>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/WorldAssembly/Chunks" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled) continue;
                    var names = new SortedSet<string>();
                    for (var t = r.transform; t && t != go.transform; t = t.parent)
                        foreach (var c in t.GetComponents<Component>())
                            if (!(c is Transform) && !(c is MeshFilter) && !(c is Renderer) && !(c is Collider)) names.Add(c.GetType().Name);
                    var top = r.transform;
                    while (top.parent && top.parent != go.transform) top = top.parent;
                    string key = top.name.Split(' ')[0] + " | " + string.Join(",", names);
                    kinds.TryGetValue(key, out int n);
                    kinds[key] = n + 1;
                }
            }
            // Triangles by part of a chunk, swamp against forest.
            var parts = new Dictionary<string, long>();
            int swampCount = 0, forestCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/WorldAssembly/Chunks" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool swamp = path.Contains("swamp") || TileShapes.IsSwampScene(System.IO.Path.GetFileNameWithoutExtension(path).Split(' ').Last());
                if (swamp) swampCount++; else forestCount++;
                foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = f.GetComponent<Renderer>();
                    if (!r || !r.enabled || !f.sharedMesh) continue;
                    string part = f.transform.IsChildOf(go.transform.Find("Ground") ?? go.transform) && go.transform.Find("Ground") ? "ground"
                        : f.GetComponentInParent<MergedLook>(true) ? (f.transform.parent.name == "Far" ? "merged far" : "merged near")
                        : f.GetComponentInParent<MergedPiece>(true) || r.gameObject.layer == 4 ? "source/water" : "other";
                    if (part == "other" && go.GetComponentInChildren<MergedLook>() is MergedLook look && look.sources.Contains(r)) part = "source";
                    string key = (swamp ? "swamp " : "forest ") + part;
                    long tris = 0;
                    for (int s = 0; s < f.sharedMesh.subMeshCount; s++) tris += f.sharedMesh.GetIndexCount(s) / 3;
                    parts.TryGetValue(key, out long n);
                    parts[key] = n + tris;
                }
            }
            sb.AppendLine($"== triangles per chunk by part (swamp {swampCount}, forest {forestCount} chunks)");
            foreach (var kv in parts.OrderBy(k => k.Key)) sb.AppendLine($"{kv.Value / (kv.Key.StartsWith("swamp") ? swampCount : forestCount),8}  {kv.Key}");
            sb.AppendLine("== renderers by group | components above");
            foreach (var kv in kinds.OrderByDescending(k => k.Value).Take(60)) sb.AppendLine($"{kv.Value,7}  {kv.Key}");
            var file = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "world-plans", "art-stats.txt");
            System.IO.File.WriteAllText(file, sb.ToString());
            return "wrote " + file;
        }

        static (int tris, int renderers, int mats) Measure(GameObject go)
        {
            int tris = 0, renderers = 0;
            var mats = new HashSet<Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (r.transform.name == "Ground") continue;
                var filter = r.GetComponent<MeshFilter>();
                var mesh = filter ? filter.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (!mesh) continue;
                renderers++;
                for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
                foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
            }
            return (tris, renderers, mats.Count);
        }
    }
}
