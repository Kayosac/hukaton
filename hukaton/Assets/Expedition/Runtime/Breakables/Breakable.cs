using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A loose thing that breaks (<see cref="BreakableKind"/>): a hard enough blow or crash and it is gone — pieces fly,
    /// dust, a crack, and now and then a few coins, a find or a bee swarm out of it. Blows come through <see cref="PropPhysics"/>
    /// (cards, blasts, landings, creatures) and crashes from its <see cref="PhysicsProp"/>. A broken one stays broken
    /// when its chunk comes back (the streamer remembers it) and is whole again at the start of the next outing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Breakable : MonoBehaviour
    {
        public BreakableKind kind;

        public bool Broken { get; private set; }

        /// <summary>
        /// Breaks it when <paramref name="speed"/> (a blow's change of speed, m/s) is enough, or whatever the speed when
        /// the blow is a card's that breaks it (<see cref="BreakableKind.BreaksFrom"/>). True when it broke.
        /// </summary>
        public bool Hit(float speed, Vector3 point, Vector3 direction, CardBlow card = default)
        {
            if (Broken || !kind || speed < kind.hitSpeed && !kind.BreaksFrom(card)) return false;
            Break(point, direction, true);
            return true;
        }

        /// <summary>Breaks it on a crash this fast (m/s). True when it broke.</summary>
        public bool Crash(float speed, Vector3 point, Vector3 direction)
        {
            if (Broken || !kind || speed < kind.crashSpeed) return false;
            Break(point, direction, true);
            return true;
        }

        /// <summary>Gone without a show or anything inside (its chunk comes back with it broken).</summary>
        public void MarkBroken()
        {
            if (Broken) return;
            Broken = true;
            MergedLooks.Unmerge(gameObject);
            gameObject.SetActive(false);
        }

        /// <summary>Whole again (a ready-made location at the start of an outing).</summary>
        public void Restore()
        {
            if (!Broken) return;
            Broken = false;
            gameObject.SetActive(true);
        }

        void Break(Vector3 point, Vector3 direction, bool loot)
        {
            var world = ExpeditionWorld.Active;
            var bounds = Bounds();
            Broken = true;
            if (world && world.Running)
            {
                var velocity = TryGetComponent(out Rigidbody body) && !body.isKinematic ? body.linearVelocity : Vector3.zero;
                PropDebris.Shatter(world, kind, bounds, direction, velocity, transform, point);
                if (loot) Spill(world, bounds.center);
                if (loot && kind.release && kind.releaseChance > 0 && Random.value < kind.releaseChance)
                    WorldCreature.Release(kind.release, world, bounds.center, transform.parent ? transform.parent : world.spawnedObjects);
            }
            // What stood on it falls (found while its colliders are still there).
            PhysicsProp.WakeResting(transform);
            MergedLooks.Unmerge(gameObject);
            gameObject.SetActive(false);
        }

        void Spill(ExpeditionWorld world, Vector3 at)
        {
            if (kind.coin && kind.coinChance > 0 && Random.value < kind.coinChance)
            {
                int count = Random.Range(kind.coins.x, kind.coins.y + 1);
                for (int i = 0; i < count; i++)
                {
                    var coin = Instantiate(kind.coin, at + Random.insideUnitSphere * .1f, Random.rotation, world.spawnedObjects);
                    coin.Launch(world, 1, Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(2.2f, 3.4f));
                }
            }
            if (kind.items && !kind.items.IsEmpty && kind.itemChance > 0 && Random.value < kind.itemChance)
            {
                var item = kind.items.Pick(Random.value);
                var drop = item ? world.SpawnLoot(item, at + Vector3.up * .2f) : null;
                if (drop) drop.Pop(Random.insideUnitSphere * .8f + Vector3.up * 3);
            }
        }

        Bounds Bounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * .4f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }

    /// <summary>A blow that came from a card: which one and at what level (none for a landing, a beast, a crash).</summary>
    public readonly struct CardBlow
    {
        public readonly string card;
        public readonly int level;

        public CardBlow(string card, int level)
        {
            this.card = card;
            this.level = level;
        }

        public bool FromCard => !string.IsNullOrEmpty(card);
    }

    /// <summary>Pieces of broken things: few at a time, none where nobody sees them.</summary>
    public static class PropDebris
    {
        public const int MaxPieces = 28;
        const float SeenWithin = 32;
        static readonly List<IceShard> Live = new List<IceShard>();
        static readonly List<int> Order = new List<int>();
        static Collider[] player;
        static int frame = -1, breaksThisFrame;

        public static int LiveCount { get { Live.RemoveAll(s => !s); return Live.Count; } }

        public static void Shatter(ExpeditionWorld world, BreakableKind kind, Bounds bounds, Vector3 direction, Vector3 velocity,
            Transform source = null, Vector3? point = null)
        {
            if (!kind) return;
            world.Spawn(kind.burst, bounds.center, Quaternion.identity, kind.burstTint, kind.burstScale * Mathf.Clamp(bounds.size.magnitude, .5f, 1.6f));
            world.Play(kind.sound, bounds.center, 1, Random.Range(.93f, 1.07f));
            if (kind.shards == null || kind.shards.Length == 0) return;
            // A blast breaking a whole camp: the first two scatter pieces, the rest just burst.
            if (Time.frameCount != frame) { frame = Time.frameCount; breaksThisFrame = 0; }
            if (++breaksThisFrame > 2) return;
            if (world.viewCamera && (bounds.center - world.viewCamera.transform.position).sqrMagnitude > SeenWithin * SeenWithin) return;
            if (player == null || player.Length == 0 || !player[0]) player = world.motor ? world.motor.GetComponentsInChildren<Collider>() : null;
            Live.RemoveAll(s => !s);
            if (kind.piecesInPlace)
            {
                if (source && kind.piecePoses != null && kind.piecePoses.Length == kind.shards.Length) TakeApart(world, kind, bounds, direction, velocity, source, point ?? bounds.center);
                return;
            }
            int count = Random.Range(kind.shardCount.x, kind.shardCount.y + 1);
            // Room is made by letting the oldest pieces go.
            while (Live.Count + count > MaxPieces && Live.Count > 0)
            {
                Live[0].Expire();
                Live.RemoveAt(0);
            }
            float size = kind.piecesAtOwnSize ? 1 : Mathf.Clamp(bounds.size.magnitude, .3f, 1.5f);
            Vector3 push = direction.sqrMagnitude > .001f ? direction.normalized * 1.2f : Vector3.zero;
            // Modelled pieces: the whole set first (a pot gives its rim, its bottom and its walls), in a random order.
            Order.Clear();
            for (int i = 0; i < kind.shards.Length; i++) Order.Add(i);
            for (int i = Order.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (Order[i], Order[j]) = (Order[j], Order[i]); }
            for (int i = 0; i < count; i++)
            {
                var prefab = kind.piecesAtOwnSize && i < Order.Count ? kind.shards[Order[i]] : kind.shards[Random.Range(0, kind.shards.Length)];
                if (!prefab) continue;
                Vector3 at = bounds.center + Vector3.Scale(Random.insideUnitSphere * .5f, bounds.extents);
                var shard = Object.Instantiate(prefab, at, Random.rotation, world.spawnedObjects);
                shard.transform.localScale = prefab.transform.localScale * size * Random.Range(kind.shardScale.x, kind.shardScale.y);
                Vector3 outward = at - bounds.center;
                outward.y = Mathf.Abs(outward.y) + .25f;
                Vector3 fly = outward.normalized * Random.Range(kind.shardSpeed.x, kind.shardSpeed.y) + push + velocity * .4f + Vector3.up;
                shard.Launch(fly, Random.insideUnitSphere * 12, Random.Range(kind.shardLifetime.x, kind.shardLifetime.y), player);
                Live.Add(shard);
            }
        }

        /// <summary>
        /// The thing comes apart where it stood (a fence: posts and rails): each piece starts in its place and the blow
        /// knocks off hardest the ones nearest to it; the rest sag and fall.
        /// </summary>
        static void TakeApart(ExpeditionWorld world, BreakableKind kind, Bounds bounds, Vector3 direction, Vector3 velocity, Transform source, Vector3 point)
        {
            int count = kind.shards.Length;
            while (Live.Count + count > MaxPieces && Live.Count > 0)
            {
                Live[0].Expire();
                Live.RemoveAt(0);
            }
            Vector3 along = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.zero;
            float reach = Mathf.Max(.75f, bounds.extents.magnitude);
            for (int i = 0; i < count; i++)
            {
                var prefab = kind.shards[i];
                if (!prefab) continue;
                var pose = kind.piecePoses[i];
                Vector3 at = source.TransformPoint(pose.position);
                var shard = Object.Instantiate(prefab, at, source.rotation * pose.rotation, world.spawnedObjects);
                shard.transform.localScale = Vector3.Scale(prefab.transform.localScale, source.lossyScale);
                // The pieces of one thing start inside each other (a rail in its post): they come apart slowly, not with a bang.
                if (shard.body) shard.body.maxDepenetrationVelocity = 1;
                float near = 1 - Mathf.Clamp01(Vector3.Distance(at, point) / (reach * 1.5f));
                Vector3 fly = along * (Random.Range(kind.shardSpeed.x, kind.shardSpeed.y) * Mathf.Lerp(.3f, 1, near))
                    + Random.insideUnitSphere * .5f + Vector3.up * (Random.Range(.4f, 1.4f) * near) + velocity * .4f;
                shard.Launch(fly, Random.insideUnitSphere * (1 + 4 * near), Random.Range(kind.shardLifetime.x, kind.shardLifetime.y), player);
                Live.Add(shard);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Live.Clear(); player = null; frame = -1; }
    }
}
