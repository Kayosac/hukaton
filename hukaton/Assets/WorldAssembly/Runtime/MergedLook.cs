using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A chunk's still dressing merged by material at build time (<c>ChunkMerge</c>): one mesh per material instead of a
    /// renderer per piece. The pieces keep their renderers, colliders and components; their renderers are kept from
    /// drawing (<see cref="Renderer.forceRenderingOff"/>) while the merged mesh draws them. A piece unmerged
    /// (<see cref="MergedPiece"/>: fire, an awakened tree) is cut out of the merged mesh (its vertices collapsed, on a
    /// copy of this chunk's mesh) and draws by its own renderers again.
    /// </summary>
    [ExecuteAlways]
    public sealed class MergedLook : MonoBehaviour
    {
        [Tooltip("The merged meshes, one per material.")]
        public MeshFilter[] parts = new MeshFilter[0];
        [Tooltip("The pieces' own renderers, kept from drawing while merged.")]
        public Renderer[] sources = new Renderer[0];
        [Tooltip("One slice per source submesh: which source, in which part, from which vertex, how many.")]
        public int[] sliceSource = new int[0], slicePart = new int[0], sliceStart = new int[0], sliceCount = new int[0];

        bool[] shown;
        Mesh[] own;
        Vector3[][] vertices;

        void OnEnable()
        {
            for (int i = 0; i < sources.Length; i++)
                if (sources[i] && !(shown != null && shown[i])) sources[i].forceRenderingOff = true;
        }

        /// <summary>Source <paramref name="source"/> draws by its own renderer from now on.</summary>
        public void Unmerge(int source)
        {
            if (source < 0 || source >= sources.Length) return;
            shown ??= new bool[sources.Length];
            if (shown[source]) return;
            shown[source] = true;
            if (sources[source]) sources[source].forceRenderingOff = false;
            for (int s = 0; s < sliceSource.Length; s++)
                if (sliceSource[s] == source) Collapse(slicePart[s], sliceStart[s], sliceCount[s]);
        }

        void Collapse(int part, int start, int count)
        {
            if (part < 0 || part >= parts.Length || !parts[part]) return;
            own ??= new Mesh[parts.Length];
            vertices ??= new Vector3[parts.Length][];
            var filter = parts[part];
            if (!own[part])
            {
                var shared = filter.sharedMesh;
                if (!shared || !shared.isReadable) return;
                own[part] = Instantiate(shared);
                own[part].name = shared.name + " (cut)";
                filter.sharedMesh = own[part];
                vertices[part] = own[part].vertices;
            }
            var v = vertices[part];
            if (start < 0 || start + count > v.Length) return;
            var at = v[start];
            for (int i = start; i < start + count; i++) v[i] = at;
            own[part].vertices = v;
        }

        void OnDestroy()
        {
            if (own == null) return;
            foreach (var mesh in own)
                if (mesh)
                {
                    if (Application.isPlaying) Destroy(mesh);
                    else DestroyImmediate(mesh);
                }
        }
    }
}
