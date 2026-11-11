using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A wet creature hit by Ice is physically encased in an ice shell: frozen until fire melts it or a physical hit
    /// shatters it (that hit deals double damage). CONTRACT STUB — implemented by the encasement task.
    /// </summary>
    public sealed class IceEncasement : MonoBehaviour
    {
        /// <summary>Encases the creature this component belongs to. True when it was encased by this call.</summary>
        public static bool TryEncase(ExpeditionWorld world, Component creature) => false;

        /// <summary>Melts encasements on <paramref name="target"/> and within <paramref name="radius"/> of the point. True when any melted.</summary>
        public static bool MeltAround(ExpeditionWorld world, Component target, Vector3 point, float radius) => false;

        /// <summary>Is this body encased right now?</summary>
        public static bool IsEncased(Component body) => false;
    }
}
