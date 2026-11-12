using System.Linq;
using System.Text;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Debug: what stands at a canonical point of a chunk prefab — the ground, the navigation nearest to it and the
    /// colliders round it. Bridge: <c>invoke Hakaton.WorldAssembly.EditorTools.ChunkProbe.At "shape variant x z [radius]"</c>.
    /// </summary>
    public static class ChunkProbe
    {
        public static string At(string arg)
        {
            var parts = arg.Split(' ');
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            var view = catalogue.Find(parts[0], parts[1]);
            if (!view) return "no such chunk";
            float x, z;
            if (parts[2] == "find")
            {
                var find = view.findSockets[int.Parse(parts[3])].localPosition;
                x = find.x + 8; z = find.z + 8;
            }
            else
            {
                x = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                z = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            }
            float r = parts.Length > 4 ? float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) : 2.5f;
            float lift = parts.Length > 5 ? float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture) : 1;
            var origin = new Vector3(50000, 0, 50000);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(view.gameObject);
            go.transform.position = origin;
            Physics.SyncTransforms();
            var nav = view.navMesh ? NavMesh.AddNavMeshData(view.navMesh, origin, Quaternion.identity) : default;
            var sb = new StringBuilder();
            try
            {
                var ground = go.transform.Find("Ground").GetComponent<Collider>();
                var p = origin + new Vector3(x - 8, 50, z - 8);
                ground.Raycast(new Ray(p, Vector3.down), out var hit, 200);
                sb.Append($"at ({x:0.0}, {z:0.0}) ground {hit.point.y - origin.y:0.00}");
                if (NavMesh.SamplePosition(hit.point + Vector3.up * (lift - 1), out var n, r, NavMesh.AllAreas)) sb.Append($", navigation at ({n.position.x - origin.x + 8:0.0}, {n.position.y - origin.y:0.00}, {n.position.z - origin.z + 8:0.0})");
                else sb.Append(", no navigation within " + r);
                foreach (var c in Physics.OverlapSphere(hit.point + Vector3.up * lift, r, ~0, QueryTriggerInteraction.Ignore).Where(c => c.transform.IsChildOf(go.transform)))
                {
                    var b = c.bounds;
                    sb.Append($"\n  {Path(c.transform, go.transform)} ({c.GetType().Name}{(c is MeshCollider m ? (m.convex ? " convex" : " mesh") : "")}) box {b.min.x - origin.x + 8:0.0}..{b.max.x - origin.x + 8:0.0}, {b.min.y - origin.y:0.0}..{b.max.y - origin.y:0.0}, {b.min.z - origin.z + 8:0.0}..{b.max.z - origin.z + 8:0.0}");
                }
            }
            finally
            {
                if (view.navMesh) NavMesh.RemoveNavMeshData(nav);
                Object.DestroyImmediate(go);
            }
            return sb.ToString();
        }

        /// <summary>Debug map of a chunk's navigation, half a metre a character, north up, from the middle of its north edge:
        /// '.' reached, 'x' walkable but cut off, ' ' nothing. "shape variant".</summary>
        public static string Map(string arg)
        {
            var parts = arg.Split(' ');
            var view = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset").Find(parts[0], parts[1]);
            var origin = new Vector3(52000, 0, 52000);
            var nav = NavMesh.AddNavMeshData(view.navMesh, origin, Quaternion.identity);
            var text = new StringBuilder("\n");
            try
            {
                var top = origin + new Vector3(0, view.edgeHeights[1], 8 - 1.1f);
                NavMesh.SamplePosition(top, out var start, 2.2f, NavMesh.AllAreas);
                var path = new NavMeshPath();
                for (int z = 31; z >= 0; z--)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        var at = origin + new Vector3(x * .5f + .25f - 8, 30, z * .5f + .25f - 8);
                        char c = ' ';
                        if (NavMesh.Raycast(at, at, out _, NavMesh.AllAreas) || true)
                        {
                            if (NavMesh.SamplePosition(at - Vector3.up * 30, out var hit, 8, NavMesh.AllAreas) && Mathf.Abs(hit.position.x - at.x) < .3f && Mathf.Abs(hit.position.z - at.z) < .3f)
                                c = NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? '.' : 'x';
                        }
                        text.Append(c);
                    }
                    text.Append('\n');
                }
            }
            finally { NavMesh.RemoveNavMeshData(nav); }
            return text.ToString();
        }

        /// <summary>Debug: how far a ForestArt piece's meshes reach out from its pivot at each height band (scale 1), "Trees/FA_TR14_KnottyGiant".</summary>
        public static string Profile(string asset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ForestArt/Prefabs/{asset}.prefab");
            var text = new StringBuilder();
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var m = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var v = filter.sharedMesh.vertices.Select(x => m.MultiplyPoint3x4(x)).ToArray();
                text.Append($"\n{filter.name}: {v.Length} vertices, {filter.sharedMesh.triangles.Length / 3} triangles");
                foreach (var (lo, hi) in new[] { (0f, .5f), (.5f, 1f), (1f, 1.5f), (1.5f, 2.2f), (2.2f, 4f), (4f, 99f) })
                {
                    var band = v.Where(x => x.y >= lo && x.y < hi).ToArray();
                    if (band.Length == 0) continue;
                    var reach = new float[8];
                    foreach (var x in band)
                    {
                        int k = (int)((Mathf.Atan2(x.z, x.x) + Mathf.PI) / (Mathf.PI * 2) * 8) % 8;
                        reach[k] = Mathf.Max(reach[k], new Vector2(x.x, x.z).magnitude);
                    }
                    text.Append($"\n  {lo:0.0}..{hi:0.0} m: " + string.Join(" ", reach.Select(r => r.ToString("0.0"))));
                }
            }
            return text.ToString();
        }

        /// <summary>Loose things (a body that rolls) set into something solid deeper than <paramref name="arg"/> m (default 0.1): they would be stuck.</summary>
        public static string Stuck(string arg)
        {
            float limit = string.IsNullOrEmpty(arg) ? .1f : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>("Assets/WorldAssembly/Chunk catalogue.asset");
            var text = new StringBuilder();
            int props = 0, stuck = 0;
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.position = new Vector3(54000, 0, 54000);
                Physics.SyncTransforms();
                try
                {
                    foreach (var prop in go.GetComponentsInChildren<Rigidbody>())
                    {
                        props++;
                        float worst = 0;
                        string with = "";
                        foreach (var mine in prop.GetComponentsInChildren<Collider>())
                            foreach (var other in Physics.OverlapBox(mine.bounds.center, mine.bounds.extents, Quaternion.identity, ~(1 << 4), QueryTriggerInteraction.Ignore))
                            {
                                if (other.transform.IsChildOf(prop.transform) || other.attachedRigidbody == prop) continue;
                                if (Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > worst)
                                { worst = depth; with = Path(other.transform, go.transform); }
                            }
                        if (worst <= limit) continue;
                        stuck++;
                        text.Append($"\n{e.shape}/{e.variant}: {Path(prop.transform, go.transform)} {worst:0.00} m into {with}");
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            return $"loose things: {props}, set into something deeper than {limit} m: {stuck}" + text;
        }

        static string Path(Transform t, Transform root)
        {
            var names = new System.Collections.Generic.List<string>();
            for (var x = t; x && x != root; x = x.parent) names.Add(x.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
