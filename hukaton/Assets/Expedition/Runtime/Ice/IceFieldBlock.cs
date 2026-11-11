using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// One square block of <see cref="IceField"/> (a few metres across): the slab mesh of the ice cells in it and the
    /// collider the player walks on. Spawned by the field from its prefab; meshes are built in world space.
    /// Vertex data for the ice shader: colour.r = how grown the slab is (0..1), colour.g = 1 on the crooked rim,
    /// colour.b = 1 on the side skirt; uv0 = world XZ in metres; uv1 = (grown, stable per-cell grain).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class IceFieldBlock : MonoBehaviour
    {
        public MeshFilter filter;
        public MeshRenderer meshRenderer;
        public MeshCollider meshCollider;

        public Vector2Int Coord { get; set; }
        /// <summary>Animating (growing or thawing) until this time: rebuilt every frame till then.</summary>
        public float AnimateUntil { get; set; }
        public float NextCollider { get; set; }
        public bool HasIce { get; private set; }

        Mesh visual, solid;

        public void Build(IceField field, bool withCollider)
        {
            if (!visual) { visual = new Mesh { name = "Ice block" }; visual.MarkDynamic(); }
            HasIce = IceSlab.Fill(visual, field, Coord, false);
            filter.sharedMesh = visual;
            if (!withCollider || !meshCollider) return;
            if (!solid) solid = new Mesh { name = "Ice block (solid)" };
            bool any = IceSlab.Fill(solid, field, Coord, true);
            meshCollider.sharedMesh = null;
            if (any) meshCollider.sharedMesh = solid;
            meshCollider.enabled = any;
        }

        public void Show(bool visible)
        {
            if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
            if (meshCollider && meshCollider.sharedMesh && meshCollider.enabled != visible) meshCollider.enabled = visible;
        }

        void OnDestroy()
        {
            if (visual) Destroy(visual);
            if (solid) Destroy(solid);
        }
    }

    /// <summary>Builds the slab mesh of one block from the field's cells.</summary>
    static class IceSlab
    {
        static readonly List<Vector3> Verts = new List<Vector3>();
        static readonly List<Color> Colours = new List<Color>();
        static readonly List<Vector2> Uv0 = new List<Vector2>(), Uv1 = new List<Vector2>();
        static readonly List<int> Tris = new List<int>();
        static readonly Dictionary<Vector2Int, int> Corners = new Dictionary<Vector2Int, int>();
        static readonly Vector2Int[] Around = { new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(0, 0) };

        public static bool Fill(Mesh mesh, IceField field, Vector2Int block, bool solidOnly)
        {
            Verts.Clear(); Colours.Clear(); Uv0.Clear(); Uv1.Clear(); Tris.Clear(); Corners.Clear();
            var cells = field.Cells;
            float now = Time.time;
            int n = field.blockCells;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var key = new Vector2Int(block.x * n + i, block.y * n + j);
                    float amount = field.AmountAt(key, now, solidOnly);
                    if (amount <= 0) continue;
                    cells.TryGet(key, out var cell);
                    int a = Corner(field, key, now, solidOnly);
                    int b = Corner(field, key + new Vector2Int(0, 1), now, solidOnly);
                    int c = Corner(field, key + new Vector2Int(1, 1), now, solidOnly);
                    int d = Corner(field, key + new Vector2Int(1, 0), now, solidOnly);
                    Tris.Add(a); Tris.Add(b); Tris.Add(c);
                    Tris.Add(a); Tris.Add(c); Tris.Add(d);
                    // The side of the slab wherever the neighbour has no ice: the rim reads as a block of ice.
                    if (field.AmountAt(key + Vector2Int.left, now, solidOnly) <= 0) Skirt(field, cell, amount, a, b, Vector3.left);
                    if (field.AmountAt(key + Vector2Int.up, now, solidOnly) <= 0) Skirt(field, cell, amount, b, c, Vector3.forward);
                    if (field.AmountAt(key + Vector2Int.right, now, solidOnly) <= 0) Skirt(field, cell, amount, c, d, Vector3.right);
                    if (field.AmountAt(key + Vector2Int.down, now, solidOnly) <= 0) Skirt(field, cell, amount, d, a, Vector3.back);
                }
            mesh.Clear();
            if (Tris.Count == 0) return false;
            mesh.SetVertices(Verts);
            mesh.SetColors(Colours);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return true;
        }

        /// <summary>A shared top vertex at a grid corner: the height of the ice around it; nudged about on the rim.</summary>
        static int Corner(IceField field, Vector2Int corner, float now, bool solidOnly)
        {
            if (Corners.TryGetValue(corner, out int index)) return index;
            float size = field.cellSize, height = 0, weight = 0, grown = 0, grain = 0;
            int present = 0;
            foreach (var offset in Around)
            {
                var key = corner + offset;
                float amount = field.AmountAt(key, now, solidOnly);
                if (amount <= 0 || !field.Cells.TryGet(key, out var cell)) continue;
                present++;
                float thick = field.thickness * (.75f + .5f * cell.grain);
                height += cell.level + thick * amount;
                grown += amount;
                grain += cell.grain;
                weight++;
            }
            height /= Mathf.Max(1, weight);
            grown /= Mathf.Max(1, weight);
            grain /= Mathf.Max(1, weight);
            var at = new Vector3(corner.x * size, height, corner.y * size);
            bool rim = present < 4;
            if (rim)
            {
                // Crooked: rim corners wander and sag a little, so the edge is never a clean staircase.
                at.x += (IceCells.Hash(corner.x, corner.y + 911) - .5f) * 2 * field.rimJitter;
                at.z += (IceCells.Hash(corner.x + 377, corner.y) - .5f) * 2 * field.rimJitter;
                at.y -= field.thickness * .3f * grown * (4 - present) / 4f;
            }
            index = Verts.Count;
            Verts.Add(at);
            Colours.Add(new Color(grown, rim ? 1 : 0, 0, 1));
            Uv0.Add(new Vector2(at.x, at.z));
            Uv1.Add(new Vector2(grown, grain));
            Corners[corner] = index;
            return index;
        }

        static void Skirt(IceField field, IceCells.Cell cell, float amount, int top0, int top1, Vector3 outward)
        {
            Vector3 a = Verts[top0], b = Verts[top1];
            float bottom = cell.level - field.skirtDepth * amount;
            var c = new Vector3(b.x, Mathf.Min(bottom, b.y - .01f), b.z);
            var d = new Vector3(a.x, Mathf.Min(bottom, a.y - .01f), a.z);
            int i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            for (int k = 0; k < 4; k++) { Colours.Add(new Color(amount, 1, 1, 1)); Uv1.Add(new Vector2(amount, cell.grain)); }
            // Along the edge and down it, so the side has its own texture space.
            float along = Vector3.Dot(new Vector3(a.x, 0, a.z), new Vector3(-outward.z, 0, outward.x));
            float along1 = Vector3.Dot(new Vector3(b.x, 0, b.z), new Vector3(-outward.z, 0, outward.x));
            Uv0.Add(new Vector2(along, a.y)); Uv0.Add(new Vector2(along1, b.y)); Uv0.Add(new Vector2(along1, c.y)); Uv0.Add(new Vector2(along, d.y));
            // Wind so the face points outward.
            var normal = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(normal, outward) >= 0) { Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2); Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3); }
            else { Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 1); Tris.Add(i); Tris.Add(i + 3); Tris.Add(i + 2); }
        }
    }
}
