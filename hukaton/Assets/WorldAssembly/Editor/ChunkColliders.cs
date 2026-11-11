using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// No invisible boxes: in every chunk prefab, a solid collider must stand where something visible is. Visible
    /// geometry (every renderer but the ground's and particles) is sampled into 0.5 m cells with its height range; each
    /// solid collider's footprint is sampled at the player's height (its bottom .. +1.8 m), and a sample with nothing
    /// visible within a cell of it counts as blocking area you cannot see.
    /// Bridge: <c>invoke Hakaton.WorldAssembly.EditorTools.ChunkColliders.Check</c>.
    /// </summary>
    public static class ChunkColliders
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";
        const float Cell = .5f;

        public static string Check(string arg) => Measure(out float _);

        /// <summary>The report; <paramref name="worstChunk"/> is the most unseen blocking area of one chunk, m².</summary>
        public static string Measure(out float worstChunk)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            var stats = new Dictionary<string, (int colliders, float hidden, string example, float worst)>();
            var perChunk = new List<(string chunk, float hidden)>();
            int prefabs = 0, solids = 0;
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab) continue;
                prefabs++;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.position = new Vector3(40000, 0, 40000);
                Physics.SyncTransforms();
                var root = go.transform;
                var ground = root.Find("Ground");
                var seen = new Dictionary<(int, int), Vector2>();
                void Mark(Vector3 p)
                {
                    var key = (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                    seen[key] = seen.TryGetValue(key, out var r) ? new Vector2(Mathf.Min(r.x, p.y), Mathf.Max(r.y, p.y)) : new Vector2(p.y, p.y);
                }
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (!mr || !mr.enabled || !mf.gameObject.activeInHierarchy || !mf.sharedMesh) continue;
                    if (ground && mf.transform.IsChildOf(ground)) continue;
                    var m = mf.transform.localToWorldMatrix;
                    var v = mf.sharedMesh.vertices;
                    var t = mf.sharedMesh.triangles;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                        float size = Mathf.Max((a - b).magnitude, (b - c).magnitude, (c - a).magnitude);
                        int steps = Mathf.Clamp(Mathf.CeilToInt(size / (Cell * .7f)), 1, 24);
                        for (int s = 0; s <= steps; s++)
                            for (int u = 0; u <= steps - s; u++)
                                Mark(a + (b - a) * ((float)s / steps) + (c - a) * ((float)u / steps));
                    }
                }
                float chunkHidden = 0;
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                {
                    if (col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                    if (ground && col.transform.IsChildOf(ground)) continue;
                    if (col.gameObject.layer == 4) continue; // the water's surface: under the water you see
                    solids++;
                    var cb = col.bounds;
                    float bottom = cb.min.y, top = Mathf.Min(cb.max.y, bottom + 1.8f);
                    int hidden = 0;
                    for (float x = cb.min.x + Cell / 2; x < cb.max.x; x += Cell)
                        for (float z = cb.min.z + Cell / 2; z < cb.max.z; z += Cell)
                        {
                            var probe = new Vector3(x, (bottom + top) / 2, z);
                            // Inside the collider (a hollow mesh only where its shell is, not in its hollow).
                            if (col is MeshCollider mc && !mc.convex)
                            {
                                if (!Physics.OverlapSphere(probe, .12f).Contains(col)) continue;
                            }
                            else if ((col.ClosestPoint(probe) - probe).sqrMagnitude > .01f) continue;
                            int kx = Mathf.FloorToInt(x / Cell), kz = Mathf.FloorToInt(z / Cell);
                            bool visible = false;
                            for (int dx = -1; dx <= 1 && !visible; dx++)
                                for (int dz = -1; dz <= 1 && !visible; dz++)
                                    if (seen.TryGetValue((kx + dx, kz + dz), out var r) && r.x <= top && r.y >= bottom) visible = true;
                            if (!visible) hidden++;
                        }
                    if (hidden == 0) continue;
                    float area = hidden * Cell * Cell;
                    chunkHidden += area;
                    string path = PathOf(col.transform, root);
                    string key = Category(path) + " (" + col.GetType().Name + ")";
                    stats.TryGetValue(key, out var st);
                    stats[key] = (st.colliders + 1, st.hidden + area, area > st.worst ? $"{e.shape}/{e.variant}: {path} {cb.size.x:0.0}x{cb.size.y:0.0}x{cb.size.z:0.0}, {hidden} cells" : st.example, Mathf.Max(st.worst, area));
                }
                perChunk.Add(($"{e.shape}/{e.variant}", chunkHidden));
                Object.DestroyImmediate(go);
            }
            worstChunk = perChunk.Count > 0 ? perChunk.Max(c => c.hidden) : 0;
            var sb = new StringBuilder($"colliders: {prefabs} chunk prefabs, {solids} solid colliders, {perChunk.Sum(c => c.hidden):0.0} m2 blocking with nothing to see, worst chunk {worstChunk:0.0} m2");
            foreach (var kv in stats.OrderByDescending(k => k.Value.hidden).Take(25))
                sb.Append($"\n{kv.Value.hidden,7:0.0} m2  {kv.Value.colliders,4}x  {kv.Key} — worst {kv.Value.example}");
            sb.Append("\nchunks: " + string.Join(", ", perChunk.OrderByDescending(c => c.hidden).Take(8).Select(c => $"{c.chunk} {c.hidden:0.0}")));
            return sb.ToString();
        }

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x && x != root; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Category(string path)
        {
            string Clean(string s) => new string(s.Where(ch => !char.IsDigit(ch)).ToArray()).Replace("(Clone)", "").Trim();
            var parts = path.Split('/').Select(Clean).ToList();
            return parts.Count <= 3 ? string.Join("/", parts) : $"{parts[0]}/{parts[1]}/…/{parts[parts.Count - 1]}";
        }
    }
}
