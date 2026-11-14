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
        public static bool IsLoose(Rigidbody body) => body && !body.GetComponent<WorldCreature>();

        /// <summary>
        /// Throws the loose things within <paramref name="radius"/> away from <paramref name="centre"/>:
        /// <paramref name="speed"/> m/s right at the centre, a third of it at the edge, tilted up by
        /// <paramref name="lift"/> (0 flat, 1 as much up as out). Returns how many moved.
        /// </summary>
        public static int Burst(Vector3 centre, float radius, float speed, float lift = .35f, Rigidbody except = null)
        {
            if (radius <= 0 || speed <= 0) return 0;
            int count = Physics.OverlapSphereNonAlloc(centre, radius, Hits, ~0, QueryTriggerInteraction.Ignore);
            Seen.Clear();
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                var body = Hits[i].attachedRigidbody;
                Hits[i] = null;
                if (!body || body == except || !Seen.Add(body) || !IsLoose(body)) continue;
                if (!Ready(body)) continue;
                Vector3 to = body.worldCenterOfMass - centre;
                float distance = to.magnitude;
                Vector3 direction = distance > .05f ? to / distance : Random.onUnitSphere;
                direction.y = Mathf.Max(0, direction.y);
                direction = (direction + Vector3.up * lift).normalized;
                float change = speed * Mathf.Lerp(1, .33f, Mathf.Clamp01(distance / radius)) * Mathf.Min(1, ReferenceMass / body.mass);
                if (body.TryGetComponent(out Breakable breakable) && breakable.Hit(change, body.worldCenterOfMass, direction)) { moved++; continue; }
                CardPhysics.Push(body, direction * (change * body.mass));
                // A little spin: things tumble instead of sliding off like pucks.
                body.AddTorque(Random.insideUnitSphere * (change * body.mass * .12f), ForceMode.Impulse);
                moved++;
            }
            Seen.Clear();
            return moved;
        }

        /// <summary>A hit on one loose body at a point (a card, a projectile): <paramref name="speed"/> m/s along the blow.</summary>
        public static bool Nudge(Rigidbody body, Vector3 point, Vector3 direction, float speed)
        {
            if (speed <= 0 || !IsLoose(body)) return false;
            if (direction.sqrMagnitude < .0001f || !Ready(body)) return false;
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
