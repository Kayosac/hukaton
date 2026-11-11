using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Ice on water, in world space: a card freezes the water inside a radius around the hit (not a whole lake mesh),
    /// and fire melts it back inside a radius. Lives on the expedition, independent of chunks and obstacles.
    /// CONTRACT STUB — implemented by the ice-field task.
    /// </summary>
    public sealed class IceField : MonoBehaviour
    {
        /// <summary>Freezes the water within <paramref name="radius"/> of <paramref name="point"/>. True when any water froze.</summary>
        public bool Freeze(Vector3 point, float radius) => false;

        /// <summary>Melts the ice within <paramref name="radius"/> of <paramref name="point"/>. True when any ice melted.</summary>
        public bool Melt(Vector3 point, float radius) => false;

        /// <summary>Removes all ice (outing start / end).</summary>
        public void Clear() { }
    }
}
