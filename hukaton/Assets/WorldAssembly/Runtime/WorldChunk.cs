using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// Root of a chunk prefab. The pivot is the chunk's centre at ground height 0; the chunk occupies
    /// size × size metres around it. Edge standard (see README): along every edge the ground is at height 0 and
    /// the middle <see cref="WorldAssembler.gateWidth"/> metres stay clear for the path; water never crosses an edge.
    /// </summary>
    public sealed class WorldChunk : MonoBehaviour
    {
        [Tooltip("Only on the start overlook: where the player stands at the beginning of an outing.")]
        public Transform departurePose;

        void OnDrawGizmos()
        {
            var assembler = GetComponentInParent<WorldAssembler>();
            float size = assembler ? assembler.chunkSize : 40f;
            Gizmos.color = new Color(1f, .8f, .3f, .6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size, .05f, size));
        }
    }
}
