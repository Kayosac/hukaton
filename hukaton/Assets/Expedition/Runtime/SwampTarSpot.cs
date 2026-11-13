using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Where a chunk of the rotten swamp has a pool of natural tar (Kayospace «Биом: Гнилая топь»): the streamer lays a
    /// tar puddle here with the rules of the «Лужа смолы» card — it slows, Fire and Lightning light it, rain washes it
    /// away, a creature set alight in it burns like a fire-vulnerable one. Burnt or washed, it stays gone for the rest
    /// of the outing, the chunk remembers.
    /// </summary>
    public sealed class SwampTarSpot : MonoBehaviour
    {
        [Tooltip("Radius of the pool, m.")]
        [Min(.3f)] public float radius = 2.5f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(.1f, .06f, .08f, .8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(1, .02f, 1));
            Gizmos.DrawWireSphere(Vector3.zero, radius);
        }
    }
}
