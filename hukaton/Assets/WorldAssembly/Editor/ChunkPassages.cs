using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Checks every chunk prefab on its own baked navigation: edge segments of one region are joined on foot, the
    /// finds of a scene can be walked to, and a scene's pocket cannot (menu Hakaton/World/Check chunk passages).
    /// </summary>
    public static class ChunkPassages
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";

        [MenuItem("Hakaton/World/Check chunk passages")]
        static void Menu() => Debug.Log(Check(""));

        static readonly Vector2[] CornerAt = { new Vector2(0, 16), new Vector2(16, 16), new Vector2(16, 0), new Vector2(0, 0) };

        public static string Check(string _)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            if (!catalogue) return "no catalogue";
            var report = new StringBuilder();
            int failures = 0, checks = 0;
            int index = 0;
            foreach (var kind in Landmarks.All) CheckLandmark(kind, catalogue, report, ref checks, ref failures, ref index);
            foreach (var entry in catalogue.entries)
            {
                if (!entry.prefab || !entry.prefab.navMesh) { report.AppendLine($"{entry.shape}/{entry.variant}: no navigation"); failures++; continue; }
                var shape = TileShapes.Get(entry.shape);
                // Landmark parts are checked together: one part alone may hold only half of a way up.
                if (shape.Kind == ShapeKind.Landmark) continue;
                var variant = shape.FindVariant(entry.variant);
                var origin = new Vector3(20000 + 40 * index++, 0, 20000);
                var instance = NavMesh.AddNavMeshData(entry.prefab.navMesh, origin, Quaternion.identity);
                try
                {
                    var ports = new List<(int region, Vector3 at, string name)>();
                    for (int side = 0; side < 4; side++)
                        for (int seg = 0; seg < 3; seg++)
                        {
                            int region = shape.Ports[side, seg];
                            if (region < 0 || shape.Gates[side, seg] != Need.Walk) continue;
                            var a = CornerAt[side];
                            var b = CornerAt[(side + 1) & 3];
                            var p = Vector2.Lerp(a, b, (seg + .5f) / 3);
                            p += (new Vector2(8, 8) - p).normalized * 1.1f;
                            float level = shape.Regions[region].Level;
                            float y = entry.prefab.edgeHeights[side * 3 + seg];
                            ports.Add((region, origin + new Vector3(p.x - 8, y, p.y - 8), $"side {side} segment {seg}"));
                        }
                    var sampled = new List<(int region, Vector3 at, string name)>();
                    foreach (var port in ports)
                    {
                        checks++;
                        if (NavMesh.SamplePosition(port.at, out var hit, 2.2f, NavMesh.AllAreas)) sampled.Add((port.region, hit.position, port.name));
                        else { failures++; report.AppendLine($"{entry.shape}/{entry.variant}: nothing walkable at {port.name}"); }
                    }
                    foreach (var group in sampled.GroupBy(s => s.region))
                    {
                        var list = group.ToList();
                        for (int i = 1; i < list.Count; i++)
                        {
                            checks++;
                            if (Walk(list[0].at, list[i].at)) continue;
                            failures++;
                            report.AppendLine($"{entry.shape}/{entry.variant}: {list[0].name} cannot walk to {list[i].name}");
                        }
                    }
                    bool scene = shape.Kind == ShapeKind.Plain || shape.Kind == ShapeKind.PlateauTop;
                    if (scene && sampled.Count > 0)
                    {
                        var from = sampled[0].at;
                        foreach (var find in entry.prefab.findSockets)
                        {
                            checks++;
                            var at = origin + find.localPosition;
                            if (NavMesh.SamplePosition(at, out var hit, .8f, NavMesh.AllAreas) && Walk(from, hit.position)) continue;
                            failures++;
                            report.AppendLine($"{entry.shape}/{entry.variant}: {find.name} cannot be walked to");
                        }
                        if (variant.ExtraRegions.Any(r => r.Pocket) && entry.prefab.pocketSocket)
                        {
                            checks++;
                            var at = origin + entry.prefab.pocketSocket.localPosition;
                            if (NavMesh.SamplePosition(at, out var hit, .5f, NavMesh.AllAreas) && Walk(from, hit.position))
                            {
                                failures++;
                                report.AppendLine($"{entry.shape}/{entry.variant}: the pocket is reached on foot");
                            }
                        }
                    }
                }
                finally { NavMesh.RemoveNavMeshData(instance); }
            }
            return $"passages: {checks} checks, {failures} failed\n" + report;
        }

        /// <summary>
        /// All parts of a landmark laid out together and joined across their cuts the way the streamer joins them:
        /// every outer edge segment and every find must be reachable on foot from the first.
        /// </summary>
        static void CheckLandmark(LandmarkKind kind, ChunkCatalog catalogue, StringBuilder report, ref int checks, ref int failures, ref int index)
        {
            int n = kind.Size;
            var origin = new Vector3(20000 + 40 * index, 0, 20000);
            index += n + 1;
            var views = new ChunkView[n, n];
            var instances = new List<NavMeshDataInstance>();
            var links = new List<NavMeshLinkInstance>();
            try
            {
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        views[i, j] = catalogue.Find(kind.PartId(i, j), "part");
                        if (!views[i, j] || !views[i, j].navMesh) { failures++; report.AppendLine($"{kind.Id}: part {i}{j} missing"); return; }
                        instances.Add(NavMesh.AddNavMeshData(views[i, j].navMesh, PartCentre(origin, i, j), Quaternion.identity));
                    }
                // Cuts: east side (1) of (i, j) against west side (3) of (i + 1, j); north (0) of (i, j) against south (2) of (i, j + 1).
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        for (int seg = 0; seg < 3; seg++)
                        {
                            if (i + 1 < n) links.Add(Link(origin, views, i, j, 1, i + 1, j, 3, seg));
                            if (j + 1 < n) links.Add(Link(origin, views, i, j, 0, i, j + 1, 2, seg));
                        }
                var points = new List<(Vector3 at, string name)>();
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        for (int side = 0; side < 4; side++)
                        {
                            bool outer = (side == 0 && j == n - 1) || (side == 1 && i == n - 1) || (side == 2 && j == 0) || (side == 3 && i == 0);
                            if (!outer) continue;
                            for (int seg = 0; seg < 3; seg++)
                            {
                                var p = SegmentPoint(side, seg, 1.1f);
                                var at = PartCentre(origin, i, j) + new Vector3(p.x - 8, views[i, j].edgeHeights[side * 3 + seg], p.y - 8);
                                checks++;
                                if (NavMesh.SamplePosition(at, out var hit, 2.2f, NavMesh.AllAreas)) points.Add((hit.position, $"part {i}{j} side {side} segment {seg}"));
                                else { failures++; report.AppendLine($"{kind.Id}: nothing walkable at part {i}{j} side {side} segment {seg}"); }
                            }
                        }
                        foreach (var find in views[i, j].findSockets)
                        {
                            checks++;
                            var at = PartCentre(origin, i, j) + find.localPosition;
                            if (NavMesh.SamplePosition(at, out var hit, .8f, NavMesh.AllAreas)) points.Add((hit.position, $"part {i}{j} {find.name}"));
                            else { failures++; report.AppendLine($"{kind.Id}: part {i}{j} {find.name} is off the navigation"); }
                        }
                    }
                for (int k = 1; k < points.Count; k++)
                {
                    checks++;
                    if (Walk(points[0].at, points[k].at)) continue;
                    failures++;
                    report.AppendLine($"{kind.Id}: {points[0].name} cannot walk to {points[k].name}");
                }
            }
            finally
            {
                foreach (var l in links) NavMesh.RemoveLink(l);
                foreach (var d in instances) NavMesh.RemoveNavMeshData(d);
            }
        }

        static Vector3 PartCentre(Vector3 origin, int i, int j) => origin + new Vector3(i * 16, 0, j * 16);

        static Vector2 SegmentPoint(int side, int seg, float inset)
        {
            var p = Vector2.Lerp(CornerAt[side], CornerAt[(side + 1) & 3], (seg + .5f) / 3);
            return p + (new Vector2(8, 8) - p).normalized * inset;
        }

        static NavMeshLinkInstance Link(Vector3 origin, ChunkView[,] views, int ai, int aj, int aSide, int bi, int bj, int bSide, int seg)
        {
            var pa = SegmentPoint(aSide, seg, .9f);
            var pb = SegmentPoint(bSide, 2 - seg, .9f);
            var data = new NavMeshLinkData
            {
                startPosition = PartCentre(origin, ai, aj) + new Vector3(pa.x - 8, views[ai, aj].edgeHeights[aSide * 3 + seg], pa.y - 8),
                endPosition = PartCentre(origin, bi, bj) + new Vector3(pb.x - 8, views[bi, bj].edgeHeights[bSide * 3 + 2 - seg], pb.y - 8),
                width = 16 / 3f * .8f,
                bidirectional = true,
                costModifier = -1,
                agentTypeID = 0,
            };
            return NavMesh.AddLink(data);
        }

        static bool Walk(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
