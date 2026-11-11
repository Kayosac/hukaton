using System.Collections;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A wet creature hit by Ice is physically encased in an ice shell (a Blender crystal chunk from
    /// <see cref="IceShellKit"/>, fitted to its body): frozen with no timer until
    /// <list type="bullet">
    /// <item>fire melts it (a Fire card on it or within its radius, burning tar): steam, the creature comes out wet;</item>
    /// <item>a physical hit (<see cref="DamageKind.Hit"/>: Strike, Heavy strike, an ally's blow) shatters it into
    /// flying shards — and that hit deals double damage;</item>
    /// <item>a killing blow of any kind breaks it.</item>
    /// </list>
    /// Lightning passes through the ice without breaking it. Added on demand to the body's root (a
    /// <see cref="WorldCreature"/>, a <see cref="ChestMimic"/>); cleared with the outing's card statuses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IceEncasement : MonoBehaviour, ICardStatus
    {
        /// <summary>The freeze an ice shell holds: long enough to mean «until it melts or shatters».</summary>
        public const float PermanentSeconds = 1e6f;
        /// <summary>A physical hit on an encased body lands this many times harder.</summary>
        public const int ShatterDamageMultiplier = 2;

        static readonly List<IceEncasement> live = new List<IceEncasement>();

        ExpeditionWorld world;
        IFreezable body;
        IceShellKit kit;
        GameObject shell;
        Vector3 shellScale, shellBase;
        float shellHeight, shellRadius;
        Coroutine routine;
        VfxBurst steam;

        /// <summary>Encased right now (also while the shell is melting).</summary>
        public bool Encased { get; private set; }
        public bool Melting { get; private set; }
        /// <summary>The shell's current object (null when not encased or no shell art is assigned).</summary>
        public GameObject Shell => shell;

        // ---------------- static API (cards, damage) ----------------

        /// <summary>
        /// Encases the body <paramref name="creature"/> belongs to. True when it was encased by this call (a melting
        /// shell freezes over again); false when it is dead, already encased or cannot freeze.
        /// </summary>
        public static bool TryEncase(ExpeditionWorld world, Component creature)
        {
            if (!world || !creature) return false;
            var freezable = creature.GetComponentInParent<IFreezable>();
            if (freezable == null || freezable is ICombatant combatant && !combatant.Alive) return false;
            var root = (Component)freezable;
            var encasement = root.GetComponent<IceEncasement>();
            if (!encasement) encasement = root.gameObject.AddComponent<IceEncasement>();
            if (encasement.Encased && !encasement.Melting) return false;
            encasement.Encase(world, freezable);
            return true;
        }

        /// <summary>Melts the shell on <paramref name="target"/> and every shell within <paramref name="radius"/> of the point.</summary>
        public static bool MeltAround(ExpeditionWorld world, Component target, Vector3 point, float radius)
        {
            bool any = false;
            var hit = Find(target);
            if (hit && hit.Melt()) any = true;
            float squared = radius * radius;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var e = live[i];
                if (!e || e == hit || !e.Encased) continue;
                if ((e.Centre - point).sqrMagnitude <= squared + e.shellRadius * e.shellRadius && e.Melt()) any = true;
            }
            return any;
        }

        /// <summary>Is this body encased right now?</summary>
        public static bool IsEncased(Component body)
        {
            var e = Find(body);
            return e && e.Encased;
        }

        /// <summary>
        /// Called by the body for every hit before it takes it; returns the damage to apply. Physical hit: the shell
        /// shatters and the damage doubles. Fire: the shell melts. Anything that kills: the shell breaks.
        /// </summary>
        public static int ResolveHit(Component body, int amount, DamageKind kind, Vector3 impulse, Vector3 point, int health)
        {
            var e = Find(body);
            if (!e || !e.Encased) return amount;
            return e.Resolve(amount, kind, impulse, point, health);
        }

        /// <summary>Breaks the shell off without a hit (an encased mimic opened): shards, no damage.</summary>
        public static void Break(Component body)
        {
            var e = Find(body);
            if (e && e.Encased) e.Shatter(Vector3.zero, e.Centre, .6f);
        }

        /// <summary>Takes the shell away at once, no effects (outing reset). Does not touch the body's freeze.</summary>
        public static void Remove(Component body)
        {
            var e = Find(body);
            if (e) e.Clear();
        }

        /// <summary>Pure rule, for tests: the damage a hit deals to an encased body.</summary>
        public static int DamageAgainstShell(int amount, DamageKind kind) => kind == DamageKind.Hit ? amount * ShatterDamageMultiplier : amount;

        static IceEncasement Find(Component body) => body ? body.GetComponentInParent<IceEncasement>() : null;

        // ---------------- instance ----------------

        Vector3 Centre => transform.position + Vector3.up * shellHeight * .45f;

        int Resolve(int amount, DamageKind kind, Vector3 impulse, Vector3 point, int health)
        {
            if (amount <= 0) return amount;
            int dealt = DamageAgainstShell(amount, kind);
            if (kind == DamageKind.Hit) Shatter(impulse, point, 1);
            else if (kind == DamageKind.Fire) { if (dealt >= health) Shatter(impulse, point, .7f); else Melt(); }
            else if (dealt >= health) Shatter(impulse, point, .8f);
            return dealt;
        }

        void Encase(ExpeditionWorld owner, IFreezable freezable)
        {
            if (owner != world)
            {
                world = owner;
                world.TrackStatus(this);
            }
            body = freezable;
            kit = world.vfx ? world.vfx.iceShell : null;
            if (routine != null) StopCoroutine(routine);
            routine = null;
            ReleaseSteam();
            bool refreeze = Encased && Melting && shell;
            Encased = true;
            Melting = false;
            if (!live.Contains(this)) live.Add(this);
            // An ice shell is the end of being wet: the water is the ice now.
            var wet = GetComponent<WetStatus>();
            if (wet) wet.Dry();
            body.Freeze(PermanentSeconds);

            Fit();
            if (!refreeze)
            {
                if (shell) Destroy(shell);
                shell = SpawnShell();
            }
            if (shell) routine = StartCoroutine(Grow(refreeze));
            if (kit)
            {
                world.Spawn(kit.encaseVfx, Centre, Quaternion.identity, kit.tint, Mathf.Clamp(shellHeight, .6f, 2f));
                world.Play(kit.encaseCue, Centre, 1, Random.Range(.9f, 1.08f));
            }
        }

        /// <summary>Measures the body: a creature knows its drawing; anything else is measured by its colliders.</summary>
        void Fit()
        {
            float height, radius;
            Vector3 centre = transform.position;
            if (body is WorldCreature creature)
            {
                height = creature.BodyHeight;
                radius = creature.BodyRadius;
            }
            else
            {
                var bounds = new Bounds(transform.position, Vector3.zero);
                foreach (var c in GetComponentsInChildren<Collider>())
                    if (c.enabled && !c.isTrigger) bounds.Encapsulate(c.bounds);
                // The model may be bigger than its colliders (a mimic's lid and straps): cover what is seen.
                foreach (var r in GetComponentsInChildren<Renderer>())
                    if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && !r.GetComponent("TMP_Text")
                        && (!shell || !r.transform.IsChildOf(shell.transform)))
                        bounds.Encapsulate(r.bounds);
                height = Mathf.Max(.3f, bounds.max.y - transform.position.y);
                radius = Mathf.Max(.2f, Mathf.Max(bounds.extents.x, bounds.extents.z));
                centre = new Vector3(bounds.center.x, transform.position.y, bounds.center.z);
            }
            float widthPadding = kit ? kit.widthPadding : 1.2f, heightPadding = kit ? kit.heightPadding : 1.3f;
            shellHeight = height * heightPadding;
            shellRadius = radius * widthPadding;
            shellBase = centre - Vector3.up * shellHeight * (kit ? kit.sink : .06f);
            shellScale = new Vector3(shellRadius * 2, shellHeight, shellRadius * 2);
        }

        GameObject SpawnShell()
        {
            if (!kit || kit.shells == null || kit.shells.Length == 0) return null;
            var prefab = kit.shells[Random.Range(0, kit.shells.Length)];
            if (!prefab) return null;
            var rotation = Quaternion.Euler(Random.Range(-kit.tilt, kit.tilt), Random.Range(0f, 360f), Random.Range(-kit.tilt, kit.tilt));
            var instance = Instantiate(prefab, shellBase, rotation, transform);
            instance.name = "Ice shell";
            // No colliders on the shell: the body under it still takes the cards and the knocks.
            foreach (var c in instance.GetComponentsInChildren<Collider>()) Destroy(c);
            return instance;
        }

        Vector3 LocalShellScale()
        {
            Vector3 parent = transform.lossyScale;
            return new Vector3(shellScale.x / Mathf.Max(.001f, parent.x), shellScale.y / Mathf.Max(.001f, parent.y), shellScale.z / Mathf.Max(.001f, parent.z));
        }

        IEnumerator Grow(bool refreeze)
        {
            Vector3 target = LocalShellScale();
            Vector3 from = refreeze ? shell.transform.localScale : new Vector3(target.x * 1.15f, target.y * .04f, target.z * 1.15f);
            float seconds = kit ? kit.growSeconds : .32f;
            float time = 0;
            while (time < seconds && shell)
            {
                time += Time.deltaTime;
                float k = Ease.OutBack(time / seconds, 1.4f);
                shell.transform.localScale = Vector3.LerpUnclamped(from, target, k);
                yield return null;
            }
            if (shell) shell.transform.localScale = target;
            routine = null;
        }

        /// <summary>Starts melting the shell. True when this call started it.</summary>
        public bool Melt()
        {
            if (!Encased || Melting) return false;
            Melting = true;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(MeltAway());
            return true;
        }

        IEnumerator MeltAway()
        {
            float seconds = kit ? kit.meltSeconds : 1.4f;
            if (kit && world)
            {
                world.Play(kit.meltCue, Centre, 1, Random.Range(.9f, 1.05f));
                steam = world.Spawn(kit.meltVfx, Centre, Quaternion.identity, Color.white, Mathf.Clamp(shellHeight, .5f, 1.8f));
                if (steam)
                {
                    steam.Follow(transform, Vector3.up * shellHeight * .5f);
                    steam.HoldFor(seconds);
                }
            }
            Vector3 from = shell ? shell.transform.localScale : Vector3.one;
            Vector3 fromPosition = shell ? shell.transform.localPosition : Vector3.zero;
            float time = 0;
            while (time < seconds)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / seconds);
                if (shell)
                {
                    // Slumps first, then thins out: the crown goes, the base spreads a little, then it is water.
                    float y = Mathf.Lerp(1, .08f, Ease.InCubic(k));
                    float xz = Mathf.Lerp(1, .82f, k) * (k > .8f ? Mathf.Lerp(1, .2f, (k - .8f) / .2f) : 1);
                    shell.transform.localScale = new Vector3(from.x * xz, from.y * y, from.z * xz);
                    shell.transform.localPosition = fromPosition + Vector3.up * Mathf.Sin(time * 40) * .004f;
                }
                yield return null;
            }
            routine = null;
            Release();
            // Meltwater: it comes out wet (Ice right away encases it again; Lightning doubles).
            if (world) WetStatus.Soak(this, world);
        }

        /// <summary>Bursts the shell into flying shards. <paramref name="force"/> scales the burst.</summary>
        void Shatter(Vector3 impulse, Vector3 point, float force)
        {
            if (!Encased) return;
            Vector3 centre = Centre;
            Bounds shellBounds = new Bounds(centre, new Vector3(shellRadius * 2, shellHeight, shellRadius * 2));
            if (shell)
            {
                var r = shell.GetComponentInChildren<Renderer>();
                if (r) shellBounds = r.bounds;
            }
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (kit && world)
            {
                SpawnShards(shellBounds, impulse, force);
                world.Spawn(kit.shatterVfx, centre, Quaternion.identity, kit.tint, Mathf.Clamp(shellHeight * force, .6f, 2.2f));
                world.Play(kit.shatterCue, centre, 1, Random.Range(.92f, 1.1f));
                if (force >= 1)
                {
                    if (world.time && kit.shatterHitStop > 0) world.time.HitStop(kit.shatterHitStop);
                    if (world.feel && kit.shatterTrauma > 0) world.feel.AddTrauma(kit.shatterTrauma);
                }
            }
            Release();
        }

        void SpawnShards(Bounds bounds, Vector3 impulse, float force)
        {
            if (kit.shards == null || kit.shards.Length == 0) return;
            int count = Mathf.RoundToInt(Random.Range(kit.shardCount.x, kit.shardCount.y + 1) * Mathf.Clamp(bounds.size.y, .6f, 1.6f) * Mathf.Lerp(.6f, 1, force));
            // Shards are cut to the shell's size: a hare's shell breaks into chips, a boar's into slabs.
            float size = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.y) * .9f, .5f, 2.5f);
            Vector3 push = new Vector3(impulse.x, 0, impulse.z);
            push = push.sqrMagnitude > .001f ? push.normalized * 2.5f : Vector3.zero;
            var player = world.motor ? world.motor.GetComponentsInChildren<Collider>() : null;
            Transform parent = world.spawnedObjects;
            for (int i = 0; i < count; i++)
            {
                var prefab = kit.shards[Random.Range(0, kit.shards.Length)];
                if (!prefab) continue;
                Vector3 at = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x) * .7f + bounds.center.x * .3f,
                    Random.Range(bounds.min.y + bounds.size.y * .1f, bounds.max.y),
                    Random.Range(bounds.min.z, bounds.max.z) * .7f + bounds.center.z * .3f);
                var shard = Instantiate(prefab, at, Random.rotation, parent);
                shard.transform.localScale = prefab.transform.localScale * size * Random.Range(.7f, 1.35f);
                Vector3 outward = at - bounds.center;
                outward.y = Mathf.Abs(outward.y) + .3f;
                Vector3 velocity = outward.normalized * Random.Range(kit.shardSpeed.x, kit.shardSpeed.y) * force + push + Vector3.up * 1.5f * force;
                shard.Launch(velocity, Random.insideUnitSphere * 14, Random.Range(kit.shardLifetime.x, kit.shardLifetime.y), player);
            }
        }

        /// <summary>The shell is gone: the body moves again.</summary>
        void Release()
        {
            bool was = Encased;
            Clear();
            if (was && body != null && !(body is Object unityObject && !unityObject)) body.Thaw();
        }

        /// <summary>Drops the shell at once, no effects, no thaw.</summary>
        void Clear()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            Encased = false;
            Melting = false;
            ReleaseSteam();
            if (shell) Destroy(shell);
            shell = null;
            live.Remove(this);
        }

        void ReleaseSteam()
        {
            if (steam) steam.Release(.5f);
            steam = null;
        }

        /// <summary>Outing over: the shell goes and the body is free.</summary>
        public void ClearStatus() => Release();

        void OnDestroy()
        {
            live.Remove(this);
            if (shell) Destroy(shell);
        }
    }
}
