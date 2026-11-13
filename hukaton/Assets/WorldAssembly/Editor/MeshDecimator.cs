using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Fewer triangles for the far look of a chunk (<see cref="ChunkMerge"/>): edges collapse, cheapest first, by the
    /// quadric error of the faces round them (Garland–Heckbert). Corners are welded by position, so flat-shaded and
    /// seamed meshes still shrink; a collapsed corner keeps its own normal and UVs at its new place. Borders are held
    /// by extra planes, and a collapse that would turn a face over is refused.
    /// </summary>
    public static class MeshDecimator
    {
        /// <summary>A one-submesh copy of <paramref name="sub"/> of <paramref name="source"/> with about <paramref name="quality"/> of its triangles.</summary>
        public static Mesh Simplify(Mesh source, int sub, float quality)
        {
            var positions = source.vertices;
            var tris = source.GetTriangles(sub);
            int target = Mathf.Max(4, Mathf.RoundToInt(tris.Length / 3 * quality));
            var corners = Collapse(positions, tris, target);
            // Only the corners still used, with their own attributes at their new places.
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors;
            var uvs = new List<Vector4>[4];
            var dims = new int[4];
            for (int c = 0; c < 4; c++)
            {
                uvs[c] = new List<Vector4>();
                if (!source.HasVertexAttribute(VertexAttribute.TexCoord0 + c)) continue;
                dims[c] = source.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + c);
                source.GetUVs(c, uvs[c]);
            }
            var remap = new Dictionary<int, int>();
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<Vector4>();
            var col = new List<Color>();
            var uv = new List<Vector4>[4];
            for (int c = 0; c < 4; c++) uv[c] = new List<Vector4>();
            var indices = new int[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                int k = corners[i];
                if (!remap.TryGetValue(k, out int j))
                {
                    j = v.Count;
                    remap[k] = j;
                    v.Add(positions[k]);
                    if (normals.Length > k) n.Add(normals[k]);
                    if (tangents.Length > k) t.Add(tangents[k]);
                    if (colors.Length > k) col.Add(colors[k]);
                    for (int c = 0; c < 4; c++) if (dims[c] > 0) uv[c].Add(uvs[c][k]);
                }
                indices[i] = j;
            }
            var mesh = new Mesh { name = source.name + " lod" };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            if (n.Count == v.Count) mesh.SetNormals(n);
            if (t.Count == v.Count) mesh.SetTangents(t);
            if (col.Count == v.Count) mesh.SetColors(col);
            for (int c = 0; c < 4; c++)
            {
                if (dims[c] == 0) continue;
                if (dims[c] <= 2) mesh.SetUVs(c, uv[c].ConvertAll(x => new Vector2(x.x, x.y)));
                else if (dims[c] == 3) mesh.SetUVs(c, uv[c].ConvertAll(x => new Vector3(x.x, x.y, x.z)));
                else mesh.SetUVs(c, uv[c]);
            }
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        sealed class Heap
        {
            readonly List<(double cost, int a, int b, int stamp)> items = new List<(double, int, int, int)>();
            public int Count => items.Count;
            public void Push((double cost, int a, int b, int stamp) x)
            {
                items.Add(x);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (items[p].cost <= items[i].cost) break;
                    (items[p], items[i]) = (items[i], items[p]);
                    i = p;
                }
            }
            public (double cost, int a, int b, int stamp) Pop()
            {
                var top = items[0];
                items[0] = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && items[l].cost < items[m].cost) m = l;
                    if (r < items.Count && items[r].cost < items[m].cost) m = r;
                    if (m == i) break;
                    (items[m], items[i]) = (items[i], items[m]);
                    i = m;
                }
                return top;
            }
        }

        /// <summary>The surviving triangles as corner vertex indices, each corner moved to its node's place (positions updated).</summary>
        static int[] Collapse(Vector3[] positions, int[] tris, int target)
        {
            // Weld corners by position into nodes.
            var nodeOf = new int[positions.Length];
            var nodes = new List<Vector3>();
            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 2000), Mathf.RoundToInt(p.y * 2000), Mathf.RoundToInt(p.z * 2000));
                if (!weld.TryGetValue(key, out int n)) { n = nodes.Count; nodes.Add(p); weld[key] = n; }
                nodeOf[i] = n;
            }
            int triCount = tris.Length / 3;
            var tn = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) tn[i] = nodeOf[tris[i]];
            var alive = new bool[triCount];
            var q = new double[nodes.Count][];
            for (int n = 0; n < nodes.Count; n++) q[n] = new double[10];
            var around = new List<int>[nodes.Count];
            for (int n = 0; n < nodes.Count; n++) around[n] = new List<int>();
            int living = 0;
            var edgeUse = new Dictionary<long, int>();
            long Edge(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            for (int t = 0; t < triCount; t++)
            {
                int a = tn[t * 3], b = tn[t * 3 + 1], c = tn[t * 3 + 2];
                if (a == b || b == c || a == c) continue;
                alive[t] = true;
                living++;
                around[a].Add(t); around[b].Add(t); around[c].Add(t);
                var normal = Vector3.Cross(nodes[b] - nodes[a], nodes[c] - nodes[a]);
                float area = normal.magnitude;
                if (area < 1e-12f) continue;
                normal /= area;
                AddPlane(q[a], normal, nodes[a], area); AddPlane(q[b], normal, nodes[a], area); AddPlane(q[c], normal, nodes[a], area);
                foreach (var e in new[] { Edge(a, b), Edge(b, c), Edge(c, a) }) { edgeUse.TryGetValue(e, out int u); edgeUse[e] = u + 1; }
            }
            // Borders: a plane through each open edge, upright to its face, keeps the outline.
            for (int t = 0; t < triCount; t++)
            {
                if (!alive[t]) continue;
                int[] v = { tn[t * 3], tn[t * 3 + 1], tn[t * 3 + 2] };
                var normal = Vector3.Cross(nodes[v[1]] - nodes[v[0]], nodes[v[2]] - nodes[v[0]]).normalized;
                for (int k = 0; k < 3; k++)
                {
                    int a = v[k], b = v[(k + 1) % 3];
                    if (edgeUse[Edge(a, b)] != 1) continue;
                    var along = nodes[b] - nodes[a];
                    var side = Vector3.Cross(along, normal).normalized;
                    float w = along.sqrMagnitude * 10;
                    AddPlane(q[a], side, nodes[a], w); AddPlane(q[b], side, nodes[a], w);
                }
            }
            var stamp = new int[nodes.Count];
            var into = new int[nodes.Count];
            for (int n = 0; n < nodes.Count; n++) into[n] = n;
            var heap = new Heap();
            void Offer(int a, int b)
            {
                var (cost, _) = Best(q[a], q[b], nodes[a], nodes[b]);
                heap.Push((cost, a, b, stamp[a] + stamp[b]));
            }
            foreach (var e in edgeUse.Keys) Offer((int)(e >> 32), (int)(e & 0xffffffff));
            var neighbours = new HashSet<int>();
            while (living > target && heap.Count > 0)
            {
                var (cost, a, b, s) = heap.Pop();
                if (into[a] != a || into[b] != b || s != stamp[a] + stamp[b]) continue;
                var (_, keepA) = Best(q[a], q[b], nodes[a], nodes[b]);
                int keep = keepA ? a : b, gone = keepA ? b : a;
                var place = nodes[keep];
                // Refuse a collapse that turns a face of the gone node over.
                bool flips = false;
                foreach (int t in around[gone])
                {
                    if (!alive[t]) continue;
                    int a0 = tn[t * 3], a1 = tn[t * 3 + 1], a2 = tn[t * 3 + 2];
                    if (a0 == keep || a1 == keep || a2 == keep) continue;
                    var before = Vector3.Cross(nodes[a1] - nodes[a0], nodes[a2] - nodes[a0]);
                    Vector3 p0 = a0 == gone ? place : nodes[a0], p1 = a1 == gone ? place : nodes[a1], p2 = a2 == gone ? place : nodes[a2];
                    var after = Vector3.Cross(p1 - p0, p2 - p0);
                    if (Vector3.Dot(before, after) <= 0) { flips = true; break; }
                }
                if (flips) continue;
                into[gone] = keep;
                for (int i = 0; i < 10; i++) q[keep][i] += q[gone][i];
                foreach (int t in around[gone])
                {
                    if (!alive[t]) continue;
                    for (int k = 0; k < 3; k++) if (tn[t * 3 + k] == gone) tn[t * 3 + k] = keep;
                    int a0 = tn[t * 3], a1 = tn[t * 3 + 1], a2 = tn[t * 3 + 2];
                    if (a0 == a1 || a1 == a2 || a0 == a2) { alive[t] = false; living--; }
                    else around[keep].Add(t);
                }
                around[gone].Clear();
                stamp[keep]++;
                neighbours.Clear();
                foreach (int t in around[keep])
                    if (alive[t])
                        for (int k = 0; k < 3; k++) if (tn[t * 3 + k] != keep) neighbours.Add(tn[t * 3 + k]);
                foreach (int n in neighbours) Offer(keep, n);
            }
            var kept = new List<int>();
            for (int t = 0; t < triCount; t++)
            {
                if (!alive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int corner = tris[t * 3 + k];
                    // The corner keeps its own attributes; its node may have moved onto another.
                    positions[corner] = nodes[tn[t * 3 + k]];
                    kept.Add(corner);
                }
            }
            return kept.ToArray();
        }

        // Symmetric 4x4 quadric as 10 numbers.
        static void AddPlane(double[] q, Vector3 n, Vector3 on, float w)
        {
            double a = n.x, b = n.y, c = n.z, d = -Vector3.Dot(n, on);
            q[0] += w * a * a; q[1] += w * a * b; q[2] += w * a * c; q[3] += w * a * d;
            q[4] += w * b * b; q[5] += w * b * c; q[6] += w * b * d;
            q[7] += w * c * c; q[8] += w * c * d; q[9] += w * d * d;
        }

        static double Error(double[] q1, double[] q2, Vector3 p)
        {
            double x = p.x, y = p.y, z = p.z;
            double e = 0;
            for (int k = 0; k < 2; k++)
            {
                var q = k == 0 ? q1 : q2;
                e += q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x
                   + q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y
                   + q[7] * z * z + 2 * q[8] * z + q[9];
            }
            return e;
        }

        static (double cost, bool keepA) Best(double[] qa, double[] qb, Vector3 a, Vector3 b)
        {
            double ea = Error(qa, qb, a), eb = Error(qa, qb, b);
            return ea <= eb ? (ea, true) : (eb, false);
        }
    }
}
