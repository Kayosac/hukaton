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
            foreach (var entry in catalogue.entries)
            {
                if (!entry.prefab || !entry.prefab.navMesh) { report.AppendLine($"{entry.shape}/{entry.variant}: no navigation"); failures++; continue; }
                var shape = TileShapes.Get(entry.shape);
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
                            float y = level >= 0 ? level * 6 : -5;
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

        static bool Walk(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
