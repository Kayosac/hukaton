using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions.Cards;
using Hakaton.OldApiary;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// Merges a chunk's still dressing by material (<see cref="MergedLook"/>): every piece that nothing moves — trees,
    /// snags, plants, stones, walls, the scene's props — is drawn as part of one mesh per material, shadows and all.
    /// Pieces that burn or wake keep a <see cref="MergedPiece"/>, so they can be cut out again when they change. What
    /// moves or plays (bodies, particles, obstacles, lights, sounds, transparent looks) is left as it is.
    /// </summary>
    public static class ChunkMerge
    {
        static readonly HashSet<Type> Still = new HashSet<Type>
        {
            typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(SphereCollider),
            typeof(CapsuleCollider), typeof(MeshCollider), typeof(Burnable), typeof(AwakenableTree),
        };

        static readonly string[] Apart = { "Ground", "Water", "Bog", "Merged look" };

        public static int Merge(GameObject chunk, string assetPath, string name)
        {
            var root = chunk.transform;
            var toChunk = root.worldToLocalMatrix;
            // Material, shadows, layer → the slices that go into one mesh.
            var groups = new Dictionary<(Material, ShadowCastingMode, bool, int), List<(int source, Mesh mesh, int sub, Matrix4x4 m)>>();
            var sources = new List<Renderer>();
            foreach (var r in chunk.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!Mergeable(r, root, out var mesh)) continue;
                int source = sources.Count;
                sources.Add(r);
                var m = toChunk * r.transform.localToWorldMatrix;
                var mats = r.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
                {
                    var key = (mats[sub], r.shadowCastingMode, r.receiveShadows, r.gameObject.layer);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<(int, Mesh, int, Matrix4x4)>();
                    list.Add((source, mesh, sub, m));
                }
            }
            if (sources.Count == 0) return 0;

            var holder = new GameObject("Merged look");
            holder.transform.SetParent(root, false);
            var look = holder.AddComponent<MergedLook>();
            // Near: the pieces as drawn; far (the chunk's LOD 1, past about 30 m): each piece with fewer triangles.
            var near = new GameObject("Near").transform;
            near.SetParent(holder.transform, false);
            var far = new GameObject("Far").transform;
            far.SetParent(holder.transform, false);
            var parts = new List<MeshFilter>();
            var sliceSource = new List<int>();
            var slicePart = new List<int>();
            var sliceStart = new List<int>();
            var sliceCount = new List<int>();
            foreach (var group in groups.OrderBy(g => g.Key.Item1.name))
            foreach (bool distant in new[] { false, true })
            {
                var (material, shadows, receive, layer) = group.Key;
                var input = distant ? group.Value.Select(s => (s.source, Lod(s.mesh, s.sub), 0, s.m)).ToList() : group.Value;
                var mesh = Build(input, $"{name} merged {material.name}{(distant ? " far" : "")}", out var slices);
                AssetDatabase.AddObjectToAsset(mesh, assetPath);
                var go = new GameObject(material.name) { layer = layer };
                go.transform.SetParent(distant ? far : near, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = shadows;
                renderer.receiveShadows = receive;
                foreach (var (source, start, count) in slices)
                {
                    sliceSource.Add(source);
                    slicePart.Add(parts.Count);
                    sliceStart.Add(start);
                    sliceCount.Add(count);
                }
                parts.Add(filter);
            }
            look.parts = parts.ToArray();
            look.sources = sources.ToArray();
            look.sliceSource = sliceSource.ToArray();
            look.slicePart = slicePart.ToArray();
            look.sliceStart = sliceStart.ToArray();
            look.sliceCount = sliceCount.ToArray();

            // The pieces that can change: the burnable or awakenable object over each source.
            var pieces = new Dictionary<GameObject, List<int>>();
            for (int i = 0; i < sources.Count; i++)
            {
                Component owner = sources[i].GetComponentInParent<Burnable>(true);
                var tree = sources[i].GetComponentInParent<AwakenableTree>(true);
                if (tree && (!owner || tree.transform.IsChildOf(owner.transform))) owner = tree;
                if (!owner) continue;
                if (!pieces.TryGetValue(owner.gameObject, out var list)) pieces[owner.gameObject] = list = new List<int>();
                list.Add(i);
            }
            foreach (var kv in pieces)
            {
                var piece = kv.Key.AddComponent<MergedPiece>();
                piece.look = look;
                piece.sources = kv.Value.ToArray();
            }
            return sources.Count;
        }

        static readonly Dictionary<(Mesh, int), Mesh> lods = new Dictionary<(Mesh, int), Mesh>();

        /// <summary>Forget the far meshes made so far (the art may have changed): at the start of a build.</summary>
        public static void ForgetLods()
        {
            foreach (var m in lods.Values) if (m) Object.DestroyImmediate(m);
            lods.Clear();
        }

        /// <summary>The far look of one submesh: small pieces as they are, big ones to a third of their triangles.</summary>
        static Mesh Lod(Mesh mesh, int sub)
        {
            if (lods.TryGetValue((mesh, sub), out var lod) && lod) return lod;
            int triangles = (int)mesh.GetIndexCount(sub) / 3;
            float quality = triangles < 120 ? 1 : triangles < 400 ? .55f : .3f;
            lod = MeshDecimator.Simplify(mesh, sub, quality);
            lods[(mesh, sub)] = lod;
            return lod;
        }

        static bool Mergeable(MeshRenderer r, Transform root, out Mesh mesh)
        {
            mesh = null;
            if (!r.enabled || r.gameObject.layer == 4) return false;
            var filter = r.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) return false;
            var mats = r.sharedMaterials;
            if (mats.Length == 0 || mats.Any(m => !m || m.renderQueue >= (int)RenderQueue.GeometryLast)) return false;
            for (var t = r.transform; t && t != root; t = t.parent)
            {
                if (t.parent == root && Apart.Contains(t.name)) return false;
                foreach (var c in t.GetComponents<Component>())
                    if (c && !Still.Contains(c.GetType())) return false;
            }
            mesh = filter.sharedMesh;
            return true;
        }

        /// <summary>One mesh of the slices, in chunk space; each slice's vertices are contiguous (start, count).</summary>
        static Mesh Build(List<(int source, Mesh mesh, int sub, Matrix4x4 m)> slices, string name, out List<(int source, int start, int count)> ranges)
        {
            ranges = new List<(int, int, int)>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uvs = new List<Vector4>[4];
            int[] dims = new int[4];
            for (int c = 0; c < 4; c++) uvs[c] = new List<Vector4>();
            bool anyColor = slices.Any(s => s.mesh.HasVertexAttribute(VertexAttribute.Color));
            bool anyTangent = slices.Any(s => s.mesh.HasVertexAttribute(VertexAttribute.Tangent));
            for (int c = 0; c < 4; c++)
                dims[c] = slices.Max(s => s.mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + c) ? s.mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + c) : 0);
            var indices = new List<int>();
            var cache = new Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector4[] t, Color[] col, List<Vector4>[] uv)>();
            foreach (var (source, mesh, sub, m) in slices)
            {
                if (!cache.TryGetValue(mesh, out var data))
                {
                    var uv = new List<Vector4>[4];
                    for (int c = 0; c < 4; c++)
                    {
                        uv[c] = new List<Vector4>();
                        if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + c)) mesh.GetUVs(c, uv[c]);
                    }
                    data = (mesh.vertices, mesh.normals, mesh.tangents, mesh.colors, uv);
                    cache[mesh] = data;
                }
                var tri = mesh.GetTriangles(sub);
                var remap = new Dictionary<int, int>();
                int start = positions.Count;
                var normalM = m.inverse.transpose;
                float handed = m.determinant < 0 ? -1 : 1;
                foreach (int i in tri)
                {
                    if (!remap.TryGetValue(i, out int j))
                    {
                        j = positions.Count;
                        remap[i] = j;
                        positions.Add(m.MultiplyPoint3x4(data.v[i]));
                        normals.Add(data.n.Length > i ? normalM.MultiplyVector(data.n[i]).normalized : Vector3.up);
                        if (anyTangent)
                        {
                            var t = data.t.Length > i ? data.t[i] : new Vector4(1, 0, 0, 1);
                            var tv = m.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                            tangents.Add(new Vector4(tv.x, tv.y, tv.z, t.w * handed));
                        }
                        if (anyColor) colors.Add(data.col.Length > i ? data.col[i] : Color.white);
                        for (int c = 0; c < 4; c++)
                            if (dims[c] > 0) uvs[c].Add(data.uv[c].Count > i ? data.uv[c][i] : Vector4.zero);
                    }
                    indices.Add(j);
                }
                // A mirrored piece turns its triangles round.
                if (handed < 0)
                    for (int k = indices.Count - tri.Length; k + 2 < indices.Count; k += 3)
                        (indices[k + 1], indices[k + 2]) = (indices[k + 2], indices[k + 1]);
                ranges.Add((source, start, positions.Count - start));
            }
            var merged = new Mesh { name = name };
            if (positions.Count > 65000) merged.indexFormat = IndexFormat.UInt32;
            merged.SetVertices(positions);
            merged.SetNormals(normals);
            if (anyTangent) merged.SetTangents(tangents);
            if (anyColor) merged.SetColors(colors);
            for (int c = 0; c < 4; c++)
            {
                if (dims[c] == 0) continue;
                if (dims[c] <= 2) merged.SetUVs(c, uvs[c].Select(u => new Vector2(u.x, u.y)).ToList());
                else if (dims[c] == 3) merged.SetUVs(c, uvs[c].Select(u => new Vector3(u.x, u.y, u.z)).ToList());
                else merged.SetUVs(c, uvs[c]);
            }
            merged.SetTriangles(indices, 0);
            merged.RecalculateBounds();
            return merged;
        }
    }
}
