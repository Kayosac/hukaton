using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Do the ground meshes of neighbouring chunks meet? Lays out the plans of a few worlds, puts each chunk's ground
    /// mesh where the streamer would (turn and level), and compares the two meshes' vertices along every shared edge:
    /// a step in height or a vertex with no partner on the other side shows as a crack in play.
    /// Bridge: <c>invoke Hakaton.WorldAssembly.EditorTools.ChunkSeams.Check [seeds]</c>.
    /// </summary>
    public static class ChunkSeams
    {
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";
        const float Tolerance = .02f;

        sealed class Edge
        {
            // Canonical edge vertices: position along the edge (0..16, x or z) → height, per canonical side.
            public readonly Dictionary<int, float>[] Sides = { new Dictionary<int, float>(), new Dictionary<int, float>(), new Dictionary<int, float>(), new Dictionary<int, float>() };
        }

        static int Key(float s) => Mathf.RoundToInt(s * 100);

        public static string Check(string arg) => Measure(int.TryParse(arg, out var n) && n > 0 ? n : 3, out _, out _);

        /// <summary>Checks the worlds of seeds 1..<paramref name="seeds"/>; the report lists the worst kinds of seam.</summary>
        public static string Measure(int seeds, out int steps, out int unmatchedEdges)
        {
            const int area = 14;
            var catalogue = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            var edges = new Dictionary<ChunkView, Edge>();
            Edge EdgeOf(ChunkView view)
            {
                if (edges.TryGetValue(view, out var e)) return e;
                e = new Edge();
                var mesh = view.transform.Find("Ground")?.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh)
                    foreach (var v in mesh.vertices)
                    {
                        // Canonical sides as world sides at turn 0: 0 north (z = +8), 1 east (x = +8), 2 south, 3 west.
                        if (Mathf.Abs(v.z - 8) < .001f) e.Sides[0][Key(v.x + 8)] = v.y;
                        if (Mathf.Abs(v.x - 8) < .001f) e.Sides[1][Key(v.z + 8)] = v.y;
                        if (Mathf.Abs(v.z + 8) < .001f) e.Sides[2][Key(v.x + 8)] = v.y;
                        if (Mathf.Abs(v.x + 8) < .001f) e.Sides[3][Key(v.z + 8)] = v.y;
                    }
                edges[view] = e;
                return e;
            }

            // World point on a chunk's world side at world position s along it (x for north/south, z for east/west),
            // back to the canonical frame: which canonical side and where along it.
            (int side, float along) Canonical(int rotation, int worldSide, float s)
            {
                // Local point of the world edge (chunk centred at 0), then turned back by the chunk's rotation.
                Vector3 local = worldSide switch
                {
                    0 => new Vector3(s - 8, 0, 8),
                    1 => new Vector3(8, 0, s - 8),
                    2 => new Vector3(s - 8, 0, -8),
                    _ => new Vector3(-8, 0, s - 8),
                };
                var canon = Quaternion.Euler(0, -90 * rotation, 0) * local;
                int side = Mathf.Abs(canon.z - 8) < .01f ? 0 : Mathf.Abs(canon.x - 8) < .01f ? 1 : Mathf.Abs(canon.z + 8) < .01f ? 2 : 3;
                float along = side == 0 || side == 2 ? canon.x + 8 : canon.z + 8;
                return (side, along);
            }

            int pairs = 0, stepped = 0, unmatched = 0;
            float worst = 0;
            var kinds = new Dictionary<string, (int count, float worst, string example)>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var map = new WorldMap(seed);
                float plateau = map.Rules.PlateauHeight;
                for (int x = -area; x <= area; x++)
                    for (int y = -area; y <= area; y++)
                    {
                        var a = map.Chunk(x, y);
                        var viewA = catalogue.Find(a.Shape.Id, a.Variant.Id);
                        if (!viewA) continue;
                        foreach (var (worldSide, dx, dy) in new[] { (0, 0, 1), (1, 1, 0) })
                        {
                            var b = map.Chunk(x + dx, y + dy);
                            var viewB = catalogue.Find(b.Shape.Id, b.Variant.Id);
                            if (!viewB) continue;
                            pairs++;
                            var ea = EdgeOf(viewA);
                            var eb = EdgeOf(viewB);
                            int opposite = (worldSide + 2) & 3;
                            float pairWorst = 0;
                            int pairMissing = 0;
                            string where = "";
                            for (int k = 0; k <= 32; k++)
                            {
                                float s = k * .5f;
                                var (sa, la) = Canonical(a.Rotation, worldSide, s);
                                var (sideB, lb) = Canonical(b.Rotation, opposite, s);
                                bool hasA = ea.Sides[sa].TryGetValue(Key(la), out float ha);
                                bool hasB = eb.Sides[sideB].TryGetValue(Key(lb), out float hb);
                                if (!hasA || !hasB) { pairMissing++; continue; }
                                float d = Mathf.Abs((ha + a.BaseLevel * plateau) - (hb + b.BaseLevel * plateau));
                                if (d > pairWorst) { pairWorst = d; where = $"{s:0.0} m along"; }
                            }
                            if (pairMissing > 0) unmatched++;
                            if (pairWorst <= Tolerance && pairMissing == 0) continue;
                            if (pairWorst > Tolerance) stepped++;
                            worst = Mathf.Max(worst, pairWorst);
                            string kind = string.CompareOrdinal(Name(a), Name(b)) < 0 ? $"{Name(a)} | {Name(b)}" : $"{Name(b)} | {Name(a)}";
                            kinds.TryGetValue(kind, out var known);
                            string example = known.example ?? $"seed {seed} {a.Coord}{(worldSide == 0 ? " north" : " east")} r{a.Rotation}/r{b.Rotation} b{a.BaseLevel}/b{b.BaseLevel}: step {pairWorst:0.000} at {where}, {pairMissing} unmatched";
                            kinds[kind] = (known.count + 1, Mathf.Max(known.worst, pairWorst), pairWorst > known.worst ? $"seed {seed} {a.Coord}{(worldSide == 0 ? " north" : " east")} r{a.Rotation}/r{b.Rotation} b{a.BaseLevel}/b{b.BaseLevel}: step {pairWorst:0.000} at {where}, {pairMissing} unmatched" : example);
                        }
                    }
            }
            steps = stepped;
            unmatchedEdges = unmatched;
            var sb = new StringBuilder($"seams: {pairs} edges, {stepped} with a step over {Tolerance} m, {unmatched} with unmatched vertices, worst {worst:0.000} m");
            foreach (var k in kinds.OrderByDescending(k => k.Value.worst).Take(40))
                sb.Append($"\n{k.Value.worst:0.000} x{k.Value.count} {k.Key} — {k.Value.example}");
            return sb.ToString();
        }

        static string Name(ChunkPlan p) => $"{p.Shape.Id}/{p.Variant.Id}";
    }
}
