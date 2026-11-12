using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.Expeditions;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Fire opens what it says it opens: in every chunk prefab with a card obstacle that burns (a thicket, a deadfall),
    /// the player's body is walked over the ground — physically, through the colliders, not by the baked navigation —
    /// from the lane to the place behind the obstacle (the pocket), before and after it is burnt. Before, he must not get
    /// there (unless another way leads round); after, he must.
    /// Bridge: <c>invoke Hakaton.WorldAssembly.EditorTools.ChunkReach.Check</c>.
    /// </summary>
    public static class ChunkReach
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";
        const float Cell = .25f, Radius = .28f, Height = 1.8f, Step = .45f;

        public static string Check(string arg)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            var sb = new StringBuilder();
            int checks = 0, failures = 0;
            foreach (var e in catalogue.entries)
            {
                if (!e.prefab || !e.prefab.pocketSocket || !e.prefab.obstacles.Any(o => o && o.kind == ObstacleKind.Thorns)) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab.gameObject);
                go.transform.position = new Vector3(40000, 0, 40000);
                Physics.SyncTransforms();
                try
                {
                    var view = go.GetComponent<ChunkView>();
                    var ground = go.transform.Find("Ground").GetComponent<Collider>();
                    var from = view.creatureSockets.Length > 0 ? view.creatureSockets[0].position : view.findSockets.First().position;
                    var to = view.pocketSocket.position;
                    bool before = Reach(go.transform, ground, from, to, out _);
                    foreach (var o in view.obstacles.Where(o => o && o.kind == ObstacleKind.Thorns))
                    {
                        o.barrier.enabled = false;
                        if (o.intact) o.intact.SetActive(false);
                        if (o.changed) o.changed.SetActive(true);
                    }
                    Physics.SyncTransforms();
                    bool after = Reach(go.transform, ground, from, to, out var stuck);
                    checks++;
                    if (!after)
                    {
                        failures++;
                        sb.Append($"\n{e.shape}/{e.variant}: burnt, the way to the pocket is still shut (got as near as {stuck:0.0} m); before it was {(before ? "open" : "shut")}");
                        foreach (var c in Blockers(go.transform, ground, to)) sb.Append($"\n    near the pocket: {c}");
                    }
                    else if (before) sb.Append($"\n{e.shape}/{e.variant}: the pocket is reached without burning anything");
                }
                finally { Object.DestroyImmediate(go); }
            }
            return $"reach: {checks} burnt obstacles, {failures} still shut" + sb;
        }

        /// <summary>Can the player's body walk on the ground from <paramref name="from"/> to <paramref name="to"/>? <paramref name="nearest"/>: how near it got, m.</summary>
        public static bool Reach(Transform chunk, Collider ground, Vector3 from, Vector3 to, out float nearest)
        {
            var b = ground.bounds;
            int nx = Mathf.CeilToInt(b.size.x / Cell), nz = Mathf.CeilToInt(b.size.z / Cell);
            var height = new float[nx, nz];
            var free = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var p = new Vector3(b.min.x + (i + .5f) * Cell, b.max.y + 5, b.min.z + (j + .5f) * Cell);
                    if (!ground.Raycast(new Ray(p, Vector3.down), out var hit, b.size.y + 10)) { height[i, j] = float.NaN; continue; }
                    height[i, j] = hit.point.y;
                    free[i, j] = Free(chunk, ground, hit.point);
                }
            (int, int) CellOf(Vector3 p) => (Mathf.Clamp(Mathf.FloorToInt((p.x - b.min.x) / Cell), 0, nx - 1), Mathf.Clamp(Mathf.FloorToInt((p.z - b.min.z) / Cell), 0, nz - 1));
            var start = CellOf(from);
            var goal = CellOf(to);
            // The pocket's own socket may stand in a thing (a chest): any free cell within a metre of it will do.
            var goals = new HashSet<(int, int)>();
            for (int i = goal.Item1 - 4; i <= goal.Item1 + 4; i++)
                for (int j = goal.Item2 - 4; j <= goal.Item2 + 4; j++)
                    if (i >= 0 && j >= 0 && i < nx && j < nz && free[i, j]) goals.Add((i, j));
            var queue = new Queue<(int, int)>();
            var seen = new bool[nx, nz];
            // Start from the free cell nearest the lane socket.
            var first = Enumerable.Range(0, 81).Select(k => (start.Item1 + k / 9 - 4, start.Item2 + k % 9 - 4))
                .Where(c => c.Item1 >= 0 && c.Item2 >= 0 && c.Item1 < nx && c.Item2 < nz && free[c.Item1, c.Item2])
                .OrderBy(c => (c.Item1 - start.Item1) * (c.Item1 - start.Item1) + (c.Item2 - start.Item2) * (c.Item2 - start.Item2)).DefaultIfEmpty((-1, -1)).First();
            nearest = float.MaxValue;
            if (first.Item1 < 0) return false;
            queue.Enqueue(first);
            seen[first.Item1, first.Item2] = true;
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                var here = new Vector3(b.min.x + (i + .5f) * Cell, height[i, j], b.min.z + (j + .5f) * Cell);
                nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(here.x, here.z), new Vector2(to.x, to.z)));
                if (goals.Contains((i, j))) return true;
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int a = i + di, c = j + dj;
                    if (a < 0 || c < 0 || a >= nx || c >= nz || seen[a, c] || !free[a, c]) continue;
                    if (Mathf.Abs(height[a, c] - height[i, j]) > Step) continue;
                    seen[a, c] = true;
                    queue.Enqueue((a, c));
                }
            }
            return false;
        }

        static readonly Collider[] Hits = new Collider[32];

        static bool Free(Transform chunk, Collider ground, Vector3 feet)
        {
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (Radius + Step), feet + Vector3.up * (Height - Radius), Radius, Hits, ~(1 << 4), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Hits[i] != ground && Hits[i].transform.IsChildOf(chunk) && !(Hits[i].attachedRigidbody && !Hits[i].attachedRigidbody.isKinematic)) return false;
            return true;
        }

        /// <summary>What stands round a point (to tell what still shuts the way).</summary>
        static IEnumerable<string> Blockers(Transform chunk, Collider ground, Vector3 at)
        {
            return Physics.OverlapSphere(at, 2.5f, ~(1 << 4), QueryTriggerInteraction.Ignore)
                .Where(c => c != ground && c.transform.IsChildOf(chunk))
                .Select(c => $"{Path(c.transform, chunk)} ({c.GetType().Name}{(c.enabled ? "" : ", off")})").Distinct().Take(12);
        }

        static string Path(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x && x != root; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
