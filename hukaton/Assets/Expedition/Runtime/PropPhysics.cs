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
            // The same blow goes through the leaves around.
            PlantReactions.Shake(centre, radius + 1, Mathf.Clamp01(speed / 5));
            int count = Physics.OverlapSphereNonAlloc(centre, radius, Hits, ~0, QueryTriggerInteraction.Ignore);
            Seen.Clear();
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                var body = Hits[i].attachedRigidbody;
                Hits[i] = null;
                if (!body || body == except || !Seen.Add(body) || !IsLoose(body)) continue;
                var prop = body.GetComponent<PhysicsProp>();
                if (prop) prop.Wake();
                if (body.isKinematic) continue;
                Vector3 to = body.worldCenterOfMass - centre;
                float distance = to.magnitude;
                Vector3 direction = distance > .05f ? to / distance : Random.onUnitSphere;
                direction.y = Mathf.Max(0, direction.y);
                direction = (direction + Vector3.up * lift).normalized;
                float change = speed * Mathf.Lerp(1, .33f, Mathf.Clamp01(distance / radius)) * Mathf.Min(1, ReferenceMass / body.mass);
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
            var prop = body.GetComponent<PhysicsProp>();
            if (prop) prop.Wake();
            if (body.isKinematic || direction.sqrMagnitude < .0001f) return false;
            float change = speed * Mathf.Min(1, ReferenceMass / body.mass);
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
