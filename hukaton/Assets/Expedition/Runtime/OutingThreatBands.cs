using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Level-design rule of a ready-made location, kept on its departure point: the closer to the start, the weaker
    /// the creatures. A creature whose home or any spawn point lies closer than a band's radius may be at most that
    /// band's threat tier; past the last band anything may live. Thematic inhabitants of points of interest are listed
    /// as exempt and stay where the level design put them. Nothing reads this while playing: it is the location's
    /// authoring rule, checked by its tests and drawn when selected (rings, and a red stalk over a creature that
    /// breaks it).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutingThreatBands : MonoBehaviour
    {
        [Serializable]
        public struct Band
        {
            [Tooltip("Horizontal distance from the departure, metres: the band is everything closer than this and not in an inner band.")]
            [Min(0)] public float radius;
            [Tooltip("Strongest threat tier allowed in the band.")]
            [Min(0)] public int maxTier;
        }

        [Serializable]
        public struct Kind
        {
            public WorldCreature prefab;
            [Tooltip("0 timid, 1 peaceful (fights back), 2 aggressive but weak, 3 swarms and territorial packs, 4 heavy aggressive, 5 the heaviest.")]
            [Min(0)] public int tier;
        }

        [Tooltip("Inner to outer: radii growing, tiers never falling. Past the last radius any tier may live.")]
        public List<Band> bands = new List<Band>
        {
            new Band { radius = 30, maxTier = 1 },
            new Band { radius = 55, maxTier = 2 },
            new Band { radius = 80, maxTier = 3 },
        };
        [Tooltip("Threat tier of each kind of creature living here; an instance is matched by its prefab's display name.")]
        public List<Kind> kinds = new List<Kind>();
        [Tooltip("Thematic inhabitants of points of interest: they stay where the level design put them, whatever the band.")]
        public List<WorldCreature> exempt = new List<WorldCreature>();

        /// <summary>The strongest tier allowed this far (horizontally) from the departure; any tier past the last band.</summary>
        public static int MaxTierAt(IReadOnlyList<Band> bands, float distance)
        {
            if (bands != null)
                for (int i = 0; i < bands.Count; i++)
                    if (distance < bands[i].radius) return bands[i].maxTier;
            return int.MaxValue;
        }

        /// <summary>Radii strictly growing outward and tiers never falling: the rule only ever gets more permissive.</summary>
        public static bool Ordered(IReadOnlyList<Band> bands)
        {
            if (bands == null) return true;
            for (int i = 1; i < bands.Count; i++)
                if (bands[i].radius <= bands[i - 1].radius || bands[i].maxTier < bands[i - 1].maxTier) return false;
            return true;
        }

        public int MaxTierAt(float distance) => MaxTierAt(bands, distance);

        /// <summary>Horizontal distance from the departure.</summary>
        public float Distance(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0;
            return offset.magnitude;
        }

        /// <summary>The nearest of the creature's home and its spawn points: the band there is the one it must respect.</summary>
        public float HomeDistance(WorldCreature creature)
        {
            float nearest = Distance(creature.transform.position);
            if (creature.spawnPoints != null)
                foreach (var point in creature.spawnPoints)
                    if (point) nearest = Mathf.Min(nearest, Distance(point.position));
            return nearest;
        }

        /// <summary>Threat tier of the creature (a prefab or an instance of one), or -1 when its kind is not listed.</summary>
        public int TierOf(WorldCreature creature)
        {
            if (!creature) return -1;
            foreach (var kind in kinds)
                if (kind.prefab && (kind.prefab == creature || !string.IsNullOrEmpty(creature.displayName) && kind.prefab.displayName == creature.displayName))
                    return kind.tier;
            return -1;
        }

        /// <summary>The creature may live where it is: exempt, of an unlisted kind, or weak enough for its band.</summary>
        public bool Fits(WorldCreature creature)
        {
            if (!creature || exempt.Contains(creature)) return true;
            int tier = TierOf(creature);
            return tier < 0 || tier <= MaxTierAt(HomeDistance(creature));
        }

        void OnDrawGizmosSelected() => DrawGizmos();

        /// <summary>The band rings around the departure, greener inside; a red stalk over every creature here that breaks them.</summary>
        public void DrawGizmos()
        {
            int strongest = 1;
            foreach (var kind in kinds) strongest = Mathf.Max(strongest, kind.tier);
            Vector3 centre = transform.position;
            foreach (var band in bands)
            {
                Gizmos.color = Color.Lerp(new Color(.3f, 1f, .35f), new Color(1f, .3f, .2f), Mathf.Clamp01(band.maxTier / (float)strongest));
                DrawRing(centre, band.radius);
            }
            Gizmos.color = Color.red;
            foreach (var creature in transform.root.GetComponentsInChildren<WorldCreature>(true))
            {
                if (Fits(creature)) continue;
                Vector3 foot = creature.transform.position, top = foot + Vector3.up * 6;
                Gizmos.DrawLine(foot, top);
                Gizmos.DrawWireSphere(top, .6f);
            }
        }

        static void DrawRing(Vector3 centre, float radius)
        {
            const int segments = 96;
            Vector3 previous = centre + new Vector3(radius, 0, 0);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
