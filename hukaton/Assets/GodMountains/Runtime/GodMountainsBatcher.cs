using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// Sews the level's kit pieces together when the level loads. Every model of the kit is made of many parts (a cliff of
    /// 18, a far massif of up to 97), each part its own renderer with two materials: thousands of draw calls. Parts that
    /// share a parent (one placed model) are combined into one mesh with one submesh per material, as they stand in this
    /// scene (moved, removed or re-materialled parts included); the parts keep their colliders, only their renderers go
    /// off. Identical placements share one combined mesh. Nothing is saved: the scene keeps its parts as authored.
    /// Skipped: skinned and animated things (the god), transparent materials, lightmapped and mirrored parts.
    /// </summary>
    public static class GodMountainsBatcher
    {
        public struct Report
        {
            public int parts, merged, groups, meshes, triangles;
            public override string ToString() =>
                $"{parts} part renderers → {merged} sewn renderers ({groups} models, {meshes} unique meshes, {triangles} triangles)";
        }

        readonly struct GroupKey
        {
            readonly Transform parent;
            readonly int layer;
            readonly ShadowCastingMode shadows;
            readonly bool receive;
            readonly uint renderingLayers;

            public GroupKey(Renderer r)
            {
                parent = r.transform.parent;
                layer = r.gameObject.layer;
                shadows = r.shadowCastingMode;
                receive = r.receiveShadows;
                renderingLayers = r.renderingLayerMask;
            }

            public override int GetHashCode() =>
                ((parent ? parent.GetInstanceID() : 0) * 397 ^ layer * 31 ^ (int)shadows * 7 ^ (receive ? 1 : 0)) ^ (int)renderingLayers;

            public override bool Equals(object obj) => obj is GroupKey k && k.parent == parent && k.layer == layer
                && k.shadows == shadows && k.receive == receive && k.renderingLayers == renderingLayers;
        }

        sealed class Built
        {
            public Mesh mesh;
            public Material[] materials;
            public int triangles;
        }

        public static Report Merge(Transform root)
        {
            var report = new Report();
            var groups = new Dictionary<GroupKey, List<MeshRenderer>>();
            var order = new List<GroupKey>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!Mergeable(r)) continue;
                var key = new GroupKey(r);
                if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new List<MeshRenderer>(); order.Add(key); }
                list.Add(r);
            }
            var cache = new Dictionary<string, Built>();
            var signature = new StringBuilder(4096);
            foreach (var key in order)
            {
                var list = groups[key];
                // one part with one material gains nothing
                if (list.Count < 2 && list[0].sharedMaterials.Length < 2) continue;
                var parent = list[0].transform.parent;
                var toParent = parent.worldToLocalMatrix;
                signature.Clear();
                foreach (var r in list)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var m = toParent * r.localToWorldMatrix;
                    signature.Append(mesh.GetInstanceID()).Append(':');
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < Mathf.Min(mats.Length, mesh.subMeshCount); i++) signature.Append(mats[i].GetInstanceID()).Append(',');
                    for (int i = 0; i < 16; i++) signature.Append(Mathf.RoundToInt(m[i] * 1000)).Append(' ');
                    signature.Append('|');
                }
                string sig = signature.ToString();
                if (!cache.TryGetValue(sig, out var built))
                {
                    built = Build(list, toParent, parent.name);
                    if (built == null) continue;
                    cache[sig] = built;
                    report.meshes++;
                }
                var first = list[0];
                var sewn = new GameObject("Сшито: " + list.Count) { layer = first.gameObject.layer };
                sewn.transform.SetParent(parent, false);
                sewn.AddComponent<MeshFilter>().sharedMesh = built.mesh;
                var renderer = sewn.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = built.materials;
                renderer.shadowCastingMode = first.shadowCastingMode;
                renderer.receiveShadows = first.receiveShadows;
                renderer.renderingLayerMask = first.renderingLayerMask;
                renderer.lightProbeUsage = first.lightProbeUsage;
                renderer.reflectionProbeUsage = first.reflectionProbeUsage;
                foreach (var r in list) r.enabled = false;
                report.parts += list.Count;
                report.merged++;
                report.groups++;
                report.triangles += built.triangles;
            }
            return report;
        }

        static bool Mergeable(MeshRenderer r)
        {
            if (!r.enabled || r.forceRenderingOff || !r.transform.parent) return false;
            if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) return false;
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter ? filter.sharedMesh : null;
            if (!mesh || !mesh.isReadable) return false;
            // anything animated moves its parts: leave it whole
            if (r.GetComponentInParent<Animator>()) return false;
            var materials = r.sharedMaterials;
            if (materials.Length == 0) return false;
            foreach (var m in materials)
                if (!m || m.renderQueue >= (int)RenderQueue.AlphaTest + 50) return false;
            // mirrored relative to its model: the winding would flip in the combined mesh
            var toParent = r.transform.parent.worldToLocalMatrix * r.localToWorldMatrix;
            return toParent.determinant > 0;
        }

        static Built Build(List<MeshRenderer> list, Matrix4x4 toParent, string name)
        {
            var materials = new List<Material>();
            var byMaterial = new List<List<CombineInstance>>();
            int vertices = 0;
            foreach (var r in list)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var mats = r.sharedMaterials;
                var m = toParent * r.localToWorldMatrix;
                int count = Mathf.Min(mats.Length, mesh.subMeshCount);
                for (int i = 0; i < count; i++)
                {
                    int slot = materials.IndexOf(mats[i]);
                    if (slot < 0) { slot = materials.Count; materials.Add(mats[i]); byMaterial.Add(new List<CombineInstance>()); }
                    byMaterial[slot].Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = m });
                }
                vertices += mesh.vertexCount;
            }
            if (materials.Count == 0) return null;
            var format = vertices > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            var parts = new CombineInstance[materials.Count];
            var temporary = new Mesh[materials.Count];
            for (int s = 0; s < materials.Count; s++)
            {
                temporary[s] = new Mesh { indexFormat = format };
                temporary[s].CombineMeshes(byMaterial[s].ToArray(), true, true);
                parts[s] = new CombineInstance { mesh = temporary[s], transform = Matrix4x4.identity };
            }
            var combined = new Mesh { name = name + " (sewn)", indexFormat = format };
            combined.CombineMeshes(parts, false, false);
            foreach (var t in temporary) Object.Destroy(t);
            combined.RecalculateBounds();
            int triangles = 0;
            for (int s = 0; s < combined.subMeshCount; s++) triangles += (int)combined.GetIndexCount(s) / 3;
            // the GPU copy is all that is needed: no CPU copy kept (web memory)
            combined.UploadMeshData(true);
            return new Built { mesh = combined, materials = materials.ToArray(), triangles = triangles };
        }
    }
}
