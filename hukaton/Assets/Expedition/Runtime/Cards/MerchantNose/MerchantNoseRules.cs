using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Merchant's nose arithmetic, apart from Unity objects (EditMode tests).</summary>
    public static class MerchantNoseRules
    {
        /// <summary>A chest standing at <paramref name="chest"/> is sniffed out from a hit at <paramref name="centre"/>: within the radius (3D).</summary>
        public static bool Reaches(Vector3 centre, Vector3 chest, float radius) => radius > 0 && (chest - centre).sqrMagnitude <= radius * radius;

        /// <summary>A pillar lit again keeps the later of its end and the new one (no adding up).</summary>
        public static float Until(float currentEnd, float now, float seconds) => Mathf.Max(currentEnd, now + Mathf.Max(0, seconds));
    }
}
