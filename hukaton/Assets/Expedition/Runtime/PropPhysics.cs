using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>What a loose thing is made of: picks its knock when it hits something (ExpeditionAudio).</summary>
    public enum PropSurface { Wood, Clay, Stone, Metal, Soft, Coin }

    /// <summary>
    /// Loose things answer what happens next to them: a landing, a blow, a card. Pushes are given as a change of speed
    /// (m/s) for a body of <see cref="ReferenceMass"/>; heavier bodies get proportionally less, lighter ones the same
    /// (a twig does not leave at bullet speed). Creatures are never moved from here: their own code staggers them.
    /// Everything goes through <see cref="CardPhysics.Push"/>, so Lightness on a body still multiplies it.
    /// </summary>
    public static class PropPhysics
    {
        /// <summary>Bodies up to this mass take a push in full.</summary>
        public const float ReferenceMass = 2;

        static readonly Collider[] Hits = new Collider[96];
        static readonly HashSet<Rigidbody> Seen = new HashSet<Rigidbody>();
        static readonly HashSet<Breakable> SeenFixed = new HashSet<Breakable>();

        /// <summary>Dormant props awake at the same time (real bodies): the rest stay still scenery until one of these rests.</summary>
        public const int MaxAwake = 48;
        const float RestAfter = 1.5f, FarAway = 60;

        // Dormant props that are real bodies right now.
        static readonly List<PhysicsProp> Stirring = new List<PhysicsProp>();
        public static int AwakeCount => Stirring.Count;

        /// <summary>
        /// A body that is not ground: it moves, or it may (a dormant prop is still only until something touches it).
        /// Placement, landing and roof checks skip these — a beast is not set down on a boulder that may roll away.
        /// </summary>
        public static bool Movable(Rigidbody body) =>
            body && (!body.isKinematic || body.TryGetComponent(out PhysicsProp prop) && prop.dormant);

        /// <summary>
        /// The body can take a push: a real body already, or a dormant prop woken for it. Use instead of a bare
        /// kinematic test wherever loose bodies are moved.
        /// </summary>
        public static bool Ready(Rigidbody body) => body && (!body.isKinematic || body.TryGetComponent(out PhysicsProp prop) && prop.Wake());

        /// <summary>Room for one more awake prop: when full, one that lies still (or the furthest one) goes to rest first.</summary>
        public static bool Admit(PhysicsProp prop)
        {
            if (Stirring.Count >= MaxAwake)
            {
                int out_ = -1;
                float furthest = 0;
                var world = ExpeditionWorld.Active;
                Vector3 eye = world && world.motor ? world.motor.transform.position : prop.transform.position;
                for (int i = 0; i < Stirring.Count; i++)
                {
                    var other = Stirring[i];
                    if (!other || !other.body || other.body.isKinematic || other.body.IsSleeping()) { out_ = i; break; }
                    float distance = (other.transform.position - eye).sqrMagnitude;
                    if (distance > furthest) { furthest = distance; out_ = i; }
                }
                // Never the one nearer the player than the newcomer: what he is looking at keeps moving.
                if (out_ < 0 || furthest > 0 && furthest <= (prop.transform.position - eye).sqrMagnitude && Stirring[out_] && !Stirring[out_].body.IsSleeping()) return false;
                if (Stirring[out_]) Stirring[out_].Rest();
                Stirring.RemoveAt(out_);
            }
            Stirring.Add(prop);
            return true;
        }

        /// <summary>A few times a second: awake props that have lain still, or are far from the player and slow, go to rest.</summary>
        public static void Tick(Vector3 eye, float seconds)
        {
            for (int i = Stirring.Count - 1; i >= 0; i--)
            {
                var prop = Stirring[i];
                if (!prop || !prop.body || prop.body.isKinematic || !prop.isActiveAndEnabled) { Stirring.RemoveAt(i); continue; }
                bool still = prop.body.IsSleeping();
                bool far = (prop.transform.position - eye).sqrMagnitude > FarAway * FarAway && prop.body.linearVelocity.sqrMagnitude < 1;
                prop.StillFor = still || far ? prop.StillFor + seconds : 0;
                if (prop.StillFor < RestAfter) continue;
                prop.Rest();
                Stirring.RemoveAt(i);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Stirring.Clear();

        /// <summary>A body cards, landings and blows may throw about: not a creature (those answer for themselves).</summary>
        public static bool IsLoose(Rigidbody body) => body && !body.TryGetComponent<WorldCreature>(out _);

        // ---------------- cards: arcade, not real ----------------

        /// <summary>A card's level makes everything it does to loose things bigger: ×1, ×1.6, ×2.5.</summary>
        public static float LevelBoost(int level) => level >= 3 ? 2.5f : level == 2 ? 1.6f : 1;

        /// <summary>
        /// How much of a card's blow gets through to a body this heavy: all of it up to <see cref="ReferenceMass"/>, then
        /// less, but even a gravestone goes (a card throws things about, it does not weigh them).
        /// </summary>
        public static float Heft(float mass) => Mathf.Clamp(Mathf.Pow(ReferenceMass / Mathf.Max(.01f, mass), .35f), .45f, 1);

        /// <summary>A thing thrown by a card goes no faster than this, m/s (it should fly, not vanish).</summary>
        public const float MaxCardSpeed = 17;

        /// <summary>
        /// A card throws a loose body: <paramref name="change"/> (m/s) along <paramref name="direction"/>, tilted up by
        /// <paramref name="lift"/>, set spinning (<paramref name="spin"/> rad/s about a random axis), marked as thrown
        /// (it hurts a creature it flies into).
        /// </summary>
        static void Throw(Rigidbody body, Vector3 point, Vector3 direction, float change, float lift, float spin, bool atPoint)
        {
            var dir = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.up;
            if (lift > 0) dir = (dir + Vector3.up * lift).normalized;
            float speed = Mathf.Min(change, MaxCardSpeed) * BodyModifierStatus.ImpulseScaleOf(body);
            if (atPoint) body.AddForceAtPosition(dir * speed * body.mass, point, ForceMode.Impulse);
            else body.AddForce(dir * speed, ForceMode.VelocityChange);
            if (spin > 0)
            {
                body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, 30);
                body.AddTorque(Random.onUnitSphere * spin, ForceMode.VelocityChange);
            }
            if (body.TryGetComponent(out PhysicsProp prop)) prop.Thrown();
        }

        /// <summary>
        /// A card's push given as an impulse (Impulse, Attraction, a draft, a blast): on a loose thing it acts as on a
        /// body of <see cref="ReferenceMass"/> (by <see cref="Heft"/>), <paramref name="fling"/> times as far, spinning.
        /// Breaks what the push would break. False for a creature or a body that cannot move: push those your own way.
        /// </summary>
        public static bool Shove(Rigidbody body, Vector3 impulse, float fling, float spin, float lift = 0)
        {
            if (!IsLoose(body) || body.TryGetComponent<WorldLoot>(out _) || !Ready(body)) return false;
            float change = impulse.magnitude / ReferenceMass * Heft(body.mass);
            if (body.TryGetComponent(out Breakable breakable) && breakable.Hit(change, body.worldCenterOfMass, impulse)) return true;
            Throw(body, body.worldCenterOfMass, impulse, change * fling, lift, spin, false);
            return true;
        }

        /// <summary>
        /// Throws the loose things within <paramref name="radius"/> away from <paramref name="centre"/>:
        /// <paramref name="speed"/> m/s right at the centre, a third of it at the edge, tilted up by
        /// <paramref name="lift"/> (0 flat, 1 as much up as out). Returns how many moved.
        /// </summary>
        /// <param name="fling">1: a plain blow (a landing, a beast). A card's throws things this many times farther, by
        /// <see cref="Heft"/> rather than by weight, and <paramref name="spin"/> sets them turning (rad/s).</param>
        public static int Burst(Vector3 centre, float radius, float speed, float lift = .35f, Rigidbody except = null, float fling = 1, float spin = 0)
        {
            if (radius <= 0 || speed <= 0) return 0;
            int count = Physics.OverlapSphereNonAlloc(centre, radius, Hits, ~0, QueryTriggerInteraction.Ignore);
            Seen.Clear();
            SeenFixed.Clear();
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                var body = hit.attachedRigidbody;
                Hits[i] = null;
                if (!body)
                {
                    // A fixed thing that breaks (a fence) takes the blow where it is nearest.
                    var fixedOne = Fixed(hit);
                    if (!fixedOne || !SeenFixed.Add(fixedOne)) continue;
                    Vector3 at = hit.bounds.ClosestPoint(centre);
                    float away = Vector3.Distance(at, centre);
                    if (fixedOne.Hit(speed * Mathf.Lerp(1, .33f, Mathf.Clamp01(away / radius)), at, away > .05f ? (at - centre) / away : Vector3.up)) moved++;
                    continue;
                }
                if (body == except || !Seen.Add(body) || !IsLoose(body)) continue;
                if (!Ready(body)) continue;
                Vector3 to = body.worldCenterOfMass - centre;
                float distance = to.magnitude;
                Vector3 direction = distance > .05f ? to / distance : Random.onUnitSphere;
                direction.y = Mathf.Max(0, direction.y);
                direction = (direction + Vector3.up * lift).normalized;
                float near = Mathf.Lerp(1, .33f, Mathf.Clamp01(distance / radius));
                if (fling > 1)
                {
                    float blow = speed * near * Heft(body.mass);
                    if (body.TryGetComponent(out Breakable struck) && struck.Hit(blow, body.worldCenterOfMass, direction)) { moved++; continue; }
                    Throw(body, body.worldCenterOfMass, direction, blow * fling, 0, spin * near, false);
                    moved++;
                    continue;
                }
                float change = speed * near * Mathf.Min(1, ReferenceMass / body.mass);
                if (body.TryGetComponent(out Breakable breakable) && breakable.Hit(change, body.worldCenterOfMass, direction)) { moved++; continue; }
                CardPhysics.Push(body, direction * (change * body.mass));
                // A little spin: things tumble instead of sliding off like pucks.
                body.AddTorque(Random.insideUnitSphere * (change * body.mass * .12f), ForceMode.Impulse);
                moved++;
            }
            Seen.Clear();
            SeenFixed.Clear();
            return moved;
        }

        /// <summary>A thing that breaks but stands fixed, without a body (a fence): the one this collider belongs to.</summary>
        public static Breakable Fixed(Collider collider)
        {
            if (!collider || collider.attachedRigidbody) return null;
            var breakable = collider.GetComponentInParent<Breakable>();
            return breakable && !breakable.Broken ? breakable : null;
        }

        /// <summary>A blow on a fixed thing that breaks (a heavy card on a fence). True when it broke.</summary>
        public static bool HitFixed(Collider collider, Vector3 point, Vector3 direction, float speed)
        {
            var breakable = Fixed(collider);
            return breakable && breakable.Hit(speed, point, direction);
        }

        /// <summary>
        /// A hit on one loose body at a point (a card, a projectile): <paramref name="speed"/> m/s along the blow. A card's
        /// hit (<paramref name="fling"/> above 1) throws it that many times farther, tilted up by <paramref name="lift"/>
        /// and spinning (<paramref name="spin"/> rad/s).
        /// </summary>
        public static bool Nudge(Rigidbody body, Vector3 point, Vector3 direction, float speed, float fling = 1, float lift = 0, float spin = 0)
        {
            if (speed <= 0 || !IsLoose(body)) return false;
            if (direction.sqrMagnitude < .0001f || !Ready(body)) return false;
            if (fling > 1)
            {
                float blow = speed * Heft(body.mass);
                if (body.TryGetComponent(out Breakable struck) && struck.Hit(blow, point, direction)) return true;
                Throw(body, point, direction, blow * fling, lift, spin, true);
                return true;
            }
            float change = speed * Mathf.Min(1, ReferenceMass / body.mass);
            if (body.TryGetComponent(out Breakable breakable) && breakable.Hit(change, point, direction)) return true;
            body.AddForceAtPosition(direction.normalized * (change * body.mass * BodyModifierStatus.ImpulseScaleOf(body)), point, ForceMode.Impulse);
            return true;
        }
    }

    /// <summary>
    /// The knock and the puff of dust when a loose thing hits something. One place for every body (props, chests,
    /// coins), with few voices at a time: a pile coming down is one clatter, not thirty sounds.
    /// </summary>
    public static class PropImpacts
    {
        /// <summary>Softer contacts are silent (a prop settling on the ground when its chunk appears is one of them).</summary>
        public const float MinSpeed = 1.4f;
        const float HearRange = 28, VoiceWindow = .1f;
        const int VoicesPerWindow = 3;

        static float windowStart = -10, nextDust = -10;
        static int voices;

        /// <summary>
        /// Called from the body's OnCollisionEnter. <paramref name="quietUntil"/> is the body's own pause between knocks
        /// (kept by the caller). Returns the speed of the contact when it was heard, 0 otherwise.
        /// </summary>
        public static float Collided(Rigidbody body, PropSurface surface, Collision collision, ref float quietUntil)
        {
            var world = ExpeditionWorld.Active;
            if (!body || !world || !world.Running || !world.cues) return 0;
            float now = Time.time;
            if (now < quietUntil) return 0;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < MinSpeed) return 0;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
            if (world.viewCamera && (point - world.viewCamera.transform.position).sqrMagnitude > HearRange * HearRange) return 0;
            quietUntil = now + .12f;
            if (now - windowStart > VoiceWindow) { windowStart = now; voices = 0; }
            if (voices >= VoicesPerWindow) return 0;
            voices++;
            // Heavier: lower and fuller.
            float weight = Mathf.InverseLerp(.2f, 4, body.mass);
            world.Play(world.cues.Impact(surface), point, Mathf.Clamp01(speed / 7) * Mathf.Lerp(.65f, 1, weight),
                Mathf.Lerp(1.15f, .85f, weight) * Random.Range(.94f, 1.06f));
            // Onto the ground or a wall, hard enough: a puff. Not for body against body (a pile would smoke).
            if (speed > 3.5f && !collision.rigidbody && surface != PropSurface.Coin && now >= nextDust && world.vfx)
            {
                nextDust = now + .15f;
                world.Spawn(world.vfx.dustPuff, point, Quaternion.identity, new Color(.8f, .72f, .6f), Mathf.Clamp(speed / 9 * Mathf.Lerp(.6f, 1.2f, weight), .3f, .9f));
            }
            return speed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { windowStart = nextDust = -10; voices = 0; }
    }
}
