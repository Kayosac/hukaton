using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Convex pieces for hollow things that move (a sunken boat, a hollow log). A moving body may have convex shapes
    /// only, and one hull round a boat or a log would shut it with an invisible lid; so the shapes follow the walls: a
    /// log's faces round its hollow, a boat's sides and bottom in slices along its length, each seat and end board
    /// across it. Points come from the drawn parts (not the grass), in the thing's own space.
    /// </summary>
    public static class ChunkHulls
    {
        public enum Shape { Tube, Boat }

        const float Overlap = 4;   // degrees a piece reaches into its neighbour's sector: the walls stay closed at the corners

        public static List<Vector3[]> Pieces(Transform root, Shape shape)
        {
            var seen = new HashSet<Vector3Int>();
            var points = new List<Vector3>();
            var triangles = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!f.sharedMesh || ChunkLooks.SoftPart(f.name)) continue;
                var v = f.sharedMesh.vertices.Select(x => root.InverseTransformPoint(f.transform.TransformPoint(x))).ToArray();
                foreach (var p in v) if (seen.Add(Vector3Int.RoundToInt(p * 1000))) points.Add(p);
                var t = f.sharedMesh.triangles;
                for (int i = 0; i + 2 < t.Length; i += 3) triangles.Add((v[t[i]], v[t[i + 1]], v[t[i + 2]]));
            }
            var pieces = (shape == Shape.Tube ? Tube(points) : Boat(points)).Where(p => p.Length >= 4).Select(Solid).ToList();
            // What the pieces leave out (a seat across the boat, a board at its end) gets pieces of its own, a hand's
            // breadth each, so nothing drawn is left without anything to hit: every face whose middle is not near one.
            var left = Uncovered(triangles.Select(x => (x.a + x.b + x.c) / 3).ToList(), pieces, .08f);
            var open = new HashSet<Vector3Int>(left.Select(Cell));
            foreach (var cell in triangles.Where(x => open.Contains(Cell((x.a + x.b + x.c) / 3)) && left.Contains((x.a + x.b + x.c) / 3))
                         .GroupBy(x => Cell((x.a + x.b + x.c) / 3)))
            {
                var corners = cell.SelectMany(x => new[] { x.a, x.b, x.c }).Distinct().ToArray();
                if (corners.Length >= 4) pieces.Add(Solid(corners));
            }
            return pieces;
        }

        static Vector3Int Cell(Vector3 p) => Vector3Int.FloorToInt(p / .35f);

        /// <summary>The points farther than <paramref name="reach"/> from every piece.</summary>
        static List<Vector3> Uncovered(List<Vector3> points, List<Vector3[]> pieces, float reach)
        {
            var holder = new GameObject("hull check") { hideFlags = HideFlags.HideAndDontSave };
            var at = new Vector3(0, -9000, 0);
            holder.transform.position = at;
            var colliders = new List<MeshCollider>();
            foreach (var piece in pieces)
            {
                var c = holder.AddComponent<MeshCollider>();
                c.sharedMesh = Mesh(piece, "check");
                c.convex = true;
                colliders.Add(c);
            }
            Physics.SyncTransforms();
            var left = new List<Vector3>();
            foreach (var p in points)
            {
                var world = p + at;
                bool covered = false;
                foreach (var c in colliders)
                    if ((Physics.ClosestPoint(world, c, at, Quaternion.identity) - world).sqrMagnitude <= reach * reach) { covered = true; break; }
                if (!covered) left.Add(p);
            }
            foreach (var c in colliders) Object.DestroyImmediate(c.sharedMesh);
            Object.DestroyImmediate(holder);
            return left;
        }

        /// <summary>A tube lying along its longest side: one piece per face round the hollow (sectors meeting at its corners).</summary>
        static List<Vector3[]> Tube(List<Vector3> points)
        {
            var b = Bounds(points);
            int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            float Angle(Vector3 p) => Mathf.Atan2(p[v] - b.center[v], p[u] - b.center[u]) * Mathf.Rad2Deg;
            const int sides = 6;
            const float sector = 360f / sides;
            // The boundaries go through the corners, where the points are.
            float offset = 0;
            int best = -1;
            for (float o = 0; o < sector; o += 1)
            {
                int near = points.Count(p => Mathf.Abs(Mathf.DeltaAngle(Angle(p) - o, Mathf.Round((Angle(p) - o) / sector) * sector)) < 3);
                if (near > best) { best = near; offset = o; }
            }
            var pieces = new List<Vector3[]>();
            for (int k = 0; k < sides; k++)
            {
                float from = offset + k * sector, mid = from + sector / 2;
                pieces.Add(points.Where(p => Mathf.Abs(Mathf.DeltaAngle(Angle(p), mid)) <= sector / 2 + Overlap).ToArray());
            }
            return pieces;
        }

        /// <summary>
        /// An open boat: slices along its length, each cut round the line along its top into five sectors (side, bilge,
        /// bottom, bilge, side); a seat across the top, well inside the walls, gets a piece of its own.
        /// </summary>
        static List<Vector3[]> Boat(List<Vector3> points)
        {
            var b = Bounds(points);
            int along = b.size.z >= b.size.x ? 2 : 0, across = along == 2 ? 0 : 2;
            float top = b.max.y, middle = b.center[across];
            float Radius(Vector3 p) => new Vector2(p[across] - middle, p.y - top).magnitude;
            float Angle(Vector3 p)
            {
                float a = Mathf.Atan2(p.y - top, p[across] - middle) * Mathf.Rad2Deg;
                return a > 0 ? (a > 90 ? -180 : 0) : a;
            }
            // How far the hull's skin is in each direction at each place along it (it narrows to the bow): what lies
            // well inside it is a seat or an end board, not a wall.
            var skin = new Dictionary<Vector2Int, float>();
            Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p[along] / .1f), Mathf.FloorToInt((Angle(p) + 180) / 15));
            foreach (var p in points)
            {
                var c = Cell(p);
                skin[c] = Mathf.Max(skin.TryGetValue(c, out float r) ? r : 0, Radius(p));
            }
            bool Inner(Vector3 p)
            {
                var c = Cell(p);
                float outer = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int da = -1; da <= 1; da++)
                        if (skin.TryGetValue(new Vector2Int(c.x + dz, c.y + da), out float r)) outer = Mathf.Max(outer, r);
                return Radius(p) < outer * .6f;
            }
            var inner = points.Where(Inner).ToList();
            var outerPoints = points.Where(p => !Inner(p)).ToList();
            const int slices = 3, sectors = 5;
            float length = b.size[along] / slices;
            var pieces = new List<Vector3[]>();
            for (int s = 0; s < slices; s++)
            {
                float from = b.min[along] + s * length - .03f, to = from + length + .06f;
                var slice = outerPoints.Where(p => p[along] >= from && p[along] <= to).ToList();
                const float sector = 180f / sectors;
                for (int k = 0; k < sectors; k++)
                {
                    float lo = -180 + k * sector - Overlap, hi = lo + sector + 2 * Overlap;
                    pieces.Add(slice.Where(p => Angle(p) >= lo && Angle(p) <= hi).ToArray());
                }
            }
            // Seats: boards across the top, inside the walls, in groups along the length; each a board the full width.
            var seats = inner.Where(p => p.y >= top - .2f).ToList();
            foreach (var group in Groups(seats.Select(p => p[along]).OrderBy(z => z).ToList(), .15f))
            {
                var those = seats.Where(p => p[along] >= group.x && p[along] <= group.y).ToList();
                // A long run is no board across: left to the walls rather than shut over with a lid.
                if (group.y - group.x > .5f || those.Count < 4) continue;
                pieces.Add(points.Where(p => p[along] >= group.x - .02f && p[along] <= group.y + .02f && p.y >= top - .22f).ToArray());
            }
            return pieces;
        }

        /// <summary>Runs of sorted values with no gap wider than <paramref name="gap"/>: (from, to).</summary>
        static IEnumerable<Vector2> Groups(List<float> sorted, float gap)
        {
            if (sorted.Count == 0) yield break;
            float start = sorted[0], last = sorted[0];
            foreach (float z in sorted.Skip(1))
            {
                if (z - last > gap) { yield return new Vector2(start, last); start = z; }
                last = z;
            }
            yield return new Vector2(start, last);
        }

        /// <summary>A piece flat in some direction gets a centimetre of thickness: a hull needs volume.</summary>
        static Vector3[] Solid(Vector3[] piece)
        {
            var b = Bounds(piece);
            var result = new List<Vector3>(piece);
            for (int a = 0; a < 3; a++)
                if (b.size[a] < .01f)
                {
                    var shift = Vector3.zero;
                    shift[a] = .01f;
                    result.AddRange(piece.Select(p => p + shift));
                }
            return result.ToArray();
        }

        static Bounds Bounds(IEnumerable<Vector3> points)
        {
            var list = points as IList<Vector3> ?? points.ToList();
            var b = new Bounds(list[0], Vector3.zero);
            foreach (var p in list) b.Encapsulate(p);
            return b;
        }

        /// <summary>A mesh of the piece's points for a convex collider (its hull is made from the points).</summary>
        public static Mesh Mesh(Vector3[] piece, string name)
        {
            var mesh = new Mesh { name = name, vertices = piece };
            var triangles = new List<int>();
            for (int i = 1; i + 1 < piece.Length; i++) { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1); }
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
