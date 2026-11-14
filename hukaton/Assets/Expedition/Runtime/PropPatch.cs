using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A loose heap of props on the ground — a pumpkin patch: copies of the given prefabs laid at random round a centre,
    /// each on the navigation, apart from each other and from spots already taken (creatures, other props), turned and
    /// sized a little differently. Used for the rat pumpkin's patch and for the empty patches of farms and villages.
    /// </summary>
    public static class PropPatch
    {
        /// <summary>Lays up to <paramref name="count"/> props round <paramref name="centre"/>; returns how many it laid.</summary>
        public static int Lay(IReadOnlyList<GameObject> props, Vector3 centre, float radius, float spacing, int count,
            Transform parent, System.Random random, List<Vector3> taken)
        {
            if (props == null || props.Count == 0 || count <= 0) return 0;
            int laid = 0;
            for (int tries = 0; laid < count && tries < count * 12; tries++)
            {
                double a = random.NextDouble() * System.Math.PI * 2;
                // Denser in the middle: the patch reads as a heap, not a ring.
                double r = radius * System.Math.Sqrt(random.NextDouble());
                var probe = centre + new Vector3((float)System.Math.Cos(a), 0, (float)System.Math.Sin(a)) * (float)r;
                if (!NavMesh.SamplePosition(probe, out var hit, 1, NavMesh.AllAreas)) continue;
                bool free = true;
                if (taken != null)
                    foreach (var other in taken)
                    {
                        var d = other - hit.position;
                        d.y = 0;
                        if (d.magnitude < spacing) { free = false; break; }
                    }
                if (!free) continue;
                var prefab = props[random.Next(props.Count)];
                if (!prefab) continue;
                var copy = Object.Instantiate(prefab, hit.position, Quaternion.Euler(0, (float)random.NextDouble() * 360, 0), parent);
                copy.transform.localScale *= .85f + (float)random.NextDouble() * .3f;
                taken?.Add(hit.position);
                laid++;
            }
            return laid;
        }
    }
}
