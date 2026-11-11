using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>What a card-made zone or object is, so other cards can find it without knowing its class.</summary>
    public enum CardZoneKind
    {
        TarPuddle,
        Rain,
        Fog,
        Updraft,
        Lure,
        BranchBridge,
        Mushroom,
        Rope,
        StoneCube,
        Other,
        /// <summary>A sheet of ice on water (<see cref="IcePatch"/>).</summary>
        Ice
    }

    /// <summary>
    /// An area or object created by a card (tar puddle, rain, fog, branch bridge…), registered in
    /// <see cref="ExpeditionWorld.Zones"/> so cards can interact: rain finds tar to wash, fire and lightning find tar
    /// and branches to ignite, fog blocks vision. Reactions are separate interfaces the same component may implement:
    /// <see cref="IIgnitable"/>, <see cref="IWashable"/>, <see cref="IVisionBlocker"/>.
    /// </summary>
    public interface ICardZone
    {
        CardZoneKind Kind { get; }
        /// <summary>World-space box around the zone (broad phase for the queries).</summary>
        Bounds Bounds { get; }
        /// <summary>Exact test: is this world point inside the zone?</summary>
        bool Contains(Vector3 point);
    }

    /// <summary>
    /// The live card zones of the current outing. Destroyed Unity objects drop out by themselves; the world clears
    /// the registry when the outing ends. Queries append to a caller's list and return how many they added.
    /// </summary>
    public sealed class CardZoneRegistry
    {
        readonly List<ICardZone> zones = new List<ICardZone>();

        /// <summary>Every live zone (dead entries are purged first). Do not add or remove while iterating.</summary>
        public IReadOnlyList<ICardZone> All
        {
            get
            {
                Purge();
                return zones;
            }
        }

        public int Count => All.Count;

        public void Add(ICardZone zone)
        {
            if (!IsDead(zone) && !zones.Contains(zone)) zones.Add(zone);
        }

        public bool Remove(ICardZone zone) => zones.Remove(zone);
        public void Clear() => zones.Clear();

        /// <summary>Zones that contain the point exactly (<see cref="ICardZone.Contains"/>), optionally of one kind.</summary>
        public int Containing(Vector3 point, List<ICardZone> results, CardZoneKind? kind = null)
        {
            int added = 0;
            foreach (var zone in All)
                if ((kind == null || zone.Kind == kind) && zone.Bounds.Contains(point) && zone.Contains(point)) { results.Add(zone); added++; }
            return added;
        }

        /// <summary>Zones whose bounds come within <paramref name="radius"/> of the point (0 = the point is inside the bounds).</summary>
        public int Near(Vector3 point, float radius, List<ICardZone> results, CardZoneKind? kind = null)
        {
            int added = 0;
            float squared = radius * radius;
            foreach (var zone in All)
                if ((kind == null || zone.Kind == kind) && zone.Bounds.SqrDistance(point) <= squared) { results.Add(zone); added++; }
            return added;
        }

        /// <summary>Zones whose bounds intersect the box (rain over a puddle).</summary>
        public int Overlapping(Bounds bounds, List<ICardZone> results, CardZoneKind? kind = null)
        {
            int added = 0;
            foreach (var zone in All)
                if ((kind == null || zone.Kind == kind) && zone.Bounds.Intersects(bounds)) { results.Add(zone); added++; }
            return added;
        }

        /// <summary>Every live zone that implements <typeparamref name="T"/> (e.g. all <see cref="IWashable"/>).</summary>
        public int OfType<T>(List<T> results) where T : class
        {
            int added = 0;
            foreach (var zone in All)
                if (zone is T match) { results.Add(match); added++; }
            return added;
        }

        /// <summary>The first zone containing the point that implements <typeparamref name="T"/>; null when none.</summary>
        public T FirstContaining<T>(Vector3 point) where T : class
        {
            foreach (var zone in All)
                if (zone is T match && zone.Bounds.Contains(point) && zone.Contains(point)) return match;
            return null;
        }

        void Purge() => zones.RemoveAll(IsDead);

        static bool IsDead(ICardZone zone) => zone == null || (zone is Object unityObject && !unityObject);
    }

    /// <summary>
    /// Base for card-made zones: a vertical cylinder of <see cref="radius"/> from <see cref="depth"/> below to
    /// <see cref="height"/> above the transform. Registers in <see cref="ExpeditionWorld.Zones"/> while enabled once
    /// <see cref="ExpeditionWorld.SpawnForOuting{T}"/> has handed it the world. Override the shape for anything else.
    /// </summary>
    public abstract class CardZoneBehaviour : MonoBehaviour, ICardZone, IOutingSpawned
    {
        [Min(.05f)] public float radius = 2;
        [Tooltip("Height of the zone above the transform: thin for a puddle, up to the cloud for rain.")]
        [Min(.01f)] public float height = .5f;
        [Tooltip("The zone also reaches this far below the transform (uneven ground).")]
        [Min(0)] public float depth = .25f;

        public abstract CardZoneKind Kind { get; }
        /// <summary>Set by <see cref="OnSpawned"/>; null for an instance that was not spawned through the world.</summary>
        public ExpeditionWorld World { get; private set; }

        public virtual Bounds Bounds
        {
            get
            {
                Vector3 p = transform.position;
                return new Bounds(new Vector3(p.x, p.y + (height - depth) * .5f, p.z), new Vector3(radius * 2, height + depth, radius * 2));
            }
        }

        public virtual bool Contains(Vector3 point)
        {
            Vector3 p = transform.position;
            float dx = point.x - p.x, dz = point.z - p.z;
            return dx * dx + dz * dz <= radius * radius && point.y >= p.y - depth && point.y <= p.y + height;
        }

        /// <summary>Called by the world right after spawning. Override to start, but call the base.</summary>
        public virtual void OnSpawned(ExpeditionWorld world)
        {
            World = world;
            if (isActiveAndEnabled) world.Zones.Add(this);
        }

        protected virtual void OnEnable()
        {
            if (World) World.Zones.Add(this);
        }

        protected virtual void OnDisable()
        {
            if (World) World.Zones.Remove(this);
        }

        protected virtual void OnDrawGizmosSelected()
        {
            var bounds = Bounds;
            Gizmos.color = new Color(.4f, .9f, 1f, .6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
