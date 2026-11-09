using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Where a ground effect of a card settles (tar, rain): the surface that was hit when it is static ground that is
    /// flat enough, otherwise the first such surface straight below the impact (the card hit a wall, a creature or a
    /// loose box). Creatures, the player, moving bodies and the card itself are never ground.
    /// </summary>
    public static class CardGround
    {
        const int Mask = ~(1 << 2); // the player rig is on Ignore Raycast
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static bool IsGround(Collider collider, Vector3 normal, float maxSlope)
        {
            if (!collider || collider.isTrigger) return false;
            var body = collider.attachedRigidbody;
            if (body && !body.isKinematic) return false;
            if (Combat.Of(collider) != null || collider.GetComponentInParent<CardProjectile>()) return false;
            return Vector3.Angle(normal, Vector3.up) <= maxSlope;
        }

        /// <summary>Finds the ground for an impact; false when there is none within <paramref name="dropDistance"/> below.</summary>
        public static bool TryFind(Collider target, Vector3 point, Vector3 normal, float maxSlope, float dropDistance,
            out Vector3 groundPoint, out Vector3 groundNormal)
        {
            groundPoint = point;
            groundNormal = Vector3.up;
            if (IsGround(target, normal, maxSlope))
            {
                groundNormal = normal.sqrMagnitude > .0001f ? normal.normalized : Vector3.up;
                return true;
            }
            // Step off the surface that was hit (a wall) and look straight down.
            Vector3 origin = point + (normal.sqrMagnitude > .0001f ? normal.normalized * .15f : Vector3.zero) + Vector3.up * .3f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, dropDistance + .3f, Mask, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance >= best || !IsGround(hit.collider, hit.normal, maxSlope)) continue;
                best = hit.distance;
                groundPoint = hit.point;
                groundNormal = hit.normal;
                found = true;
            }
            return found;
        }
    }
}
