using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// What you see is what you hit: in every chunk prefab, the surface of everything drawn solid (all but the ground,
    /// the water (its surface is its own collider), grass and soft plants, decals and effects) is sampled, and a sample with no collider within
    /// <see cref="Tolerance"/> of it is surface a card, a rope or the player would pass through. Reported by piece.
    /// Bridge: <c>invoke Hakaton.WorldAssembly.EditorTools.ChunkLooks.Check</c>.
    /// </summary>
    public static class ChunkLooks
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";
        public const float Tolerance = .15f;

        public static string Check(string arg) => Measure(out _, arg == "all");

        /// <summary>The report; <paramref name="worstShare"/> is the largest share of a solid piece's surface with nothing to hit there.</summary>
        public static string Measure(out float worstShare, bool listAll = false)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            var stats = new Dictionary<string, (int count, double area, double open, HashSet<string> colliders, string example, double worst)>();
            int prefabs = 0;
            var probe = new Collider[8];
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab) continue;
                prefabs++;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.position = new Vector3(40000, 0, 40000);
                Physics.SyncTransforms();
                var root = go.transform;
                var ground = root.Find("Ground");
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!renderer || !renderer.enabled || !filter.sharedMesh || filter.gameObject.layer == 4) continue;
                    if (ground && filter.transform.IsChildOf(ground)) continue;
                    string key = Key(filter.transform, root);
                    if (Soft(key)) continue;
                    var m = filter.transform.localToWorldMatrix;
                    var v = filter.sharedMesh.vertices;
                    var t = filter.sharedMesh.triangles;
                    int triangles = t.Length / 3, stride = Mathf.Max(1, triangles / 500);
                    double area = 0, open = 0;
                    for (int i = 0; i < triangles; i += stride)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[t[i * 3]]), b = m.MultiplyPoint3x4(v[t[i * 3 + 1]]), c = m.MultiplyPoint3x4(v[t[i * 3 + 2]]);
                        double s = Vector3.Cross(b - a, c - a).magnitude * .5 * stride;
                        area += s;
                        var centre = (a + b + c) / 3;
                        if (!Physics.CheckSphere(centre, Tolerance, ~(1 << 4), QueryTriggerInteraction.Ignore)) open += s;
                    }
                    var colliderKinds = new HashSet<string>(filter.GetComponentsInParent<Collider>(true).Concat(filter.GetComponentsInChildren<Collider>(true))
                        .Where(x => !x.isTrigger).Select(x => x is MeshCollider mc ? (mc.convex ? "convex" : "mesh") : x.GetType().Name.Replace("Collider", "")));
                    stats.TryGetValue(key, out var st);
                    var kinds = st.colliders ?? new HashSet<string>();
                    kinds.UnionWith(colliderKinds);
                    double share = area > 0 ? open / area : 0;
                    stats[key] = (st.count + 1, st.area + area, st.open + open, kinds, share > st.worst ? $"{e.shape}/{e.variant}" : st.example, System.Math.Max(st.worst, share));
                }
                Object.DestroyImmediate(go);
            }
            worstShare = stats.Count > 0 ? (float)stats.Values.Where(s => s.area > .5).Select(s => s.open / s.area).DefaultIfEmpty(0).Max() : 0;
            double total = stats.Values.Sum(s => s.area), opened = stats.Values.Sum(s => s.open);
            var sb = new StringBuilder($"looks: {prefabs} chunk prefabs, solid surface {total:0} m2, {opened:0} m2 ({opened / System.Math.Max(1, total) * 100:0.0}%) with nothing to hit within {Tolerance} m");
            foreach (var kv in stats.OrderByDescending(k => k.Value.open).Take(listAll ? 400 : 45))
                sb.Append($"\n{kv.Value.open,8:0.0} m2 {kv.Value.open / System.Math.Max(.001, kv.Value.area) * 100,5:0}% {kv.Value.count,5}x  {kv.Key}  [{string.Join(",", kv.Value.colliders)}]  worst {kv.Value.example} {kv.Value.worst * 100:0}%");
            return sb.ToString();
        }

        /// <summary>The piece: its ForestArt prefab and the part of it ("Trees/FA_TR01_ThickCrooked/TR01_Crown"), else its name.</summary>
        public static string Key(Transform t, Transform root)
        {
            var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) == root.gameObject
                ? NearestNested(t, root) : PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
            if (instance && instance.transform != root)
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                const string art = "Assets/ForestArt/Prefabs/";
                if (path.StartsWith(art)) path = path.Substring(art.Length);
                path = path.Replace(".prefab", "");
                return t == instance.transform ? path : path + "/" + Clean(t.name);
            }
            return Clean(t.name);
        }

        static GameObject NearestNested(Transform t, Transform root)
        {
            for (var x = t; x && x != root; x = x.parent)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(x.gameObject)) return x.gameObject;
            return null;
        }

        static string Clean(string s) => s.Replace("(Clone)", "").Trim();

        static readonly string[] SoftPieces =
        {
            "Plants/FA_VG01", "Plants/FA_VG02", "Plants/FA_VG03", "Plants/FA_VG04", "Plants/FA_VG05", "Plants/FA_VG06", "Plants/FA_VG07",
            "Plants/FA_VG08", "Plants/FA_VG09", "Plants/FA_VG11", "Plants/FA_VG12", "Plants/FA_VG13", "Plants/FA_VG14", "Plants/FA_VG17",
            "Plants/FA_VG18", "Plants/FA_VG19", "Plants/FA_VG20", "Special/FA_SP01", "Special/FA_SP02", "Special/FA_SP03",
            "Markers/FA_MK07", "Markers/FA_MK08", "Markers/FA_MK09", "Markers/FA_MK10", "Markers/FA_MK11", "Markers/FA_MK17", "Decals/", "VFX/",
        };

        /// <summary>
        /// Things you walk through as you see them: grass, ferns, broad leaves, bushes, ivy, moss, cobwebs, cloth and
        /// charms on strings, decals, effects. Everything else drawn is solid.
        /// </summary>
        public static bool Soft(string key) => key == "Water mesh" || SoftPieces.Any(key.StartsWith);
    }
}
