using System.Collections;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// One coin thrown out of a chest: flies, bounces and lies on the ground for a moment, then the bag pulls it in
    /// once the player is near and it is credited to the outing's purse (<see cref="ExpeditionWorld.CollectCoins"/>).
    /// A coin never gets lost: after <see cref="homeAfter"/> seconds it finds the player wherever they are.
    /// The look is a Retro Arsenal coin under the Visual child.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorldCoin : MonoBehaviour
    {
        public Rigidbody body;
        [Tooltip("What the coin looks like; shrinks as it flies into the bag.")]
        public Transform visual;
        [Tooltip("Coins this piece is worth (a chest overrides it when it throws the coin).")]
        [Min(1)] public int value = 1;
        [Tooltip("Toast and sparkle colour when it is picked up.")]
        public Color colour = new Color(1f, .82f, .35f);
        [Tooltip("Seconds after landing out of the chest before the bag can pull it in.")]
        [Min(0)] public float magnetDelay = 1.1f;
        [Tooltip("How close the player's body must be for the bag to pull it in, m.")]
        [Min(0)] public float magnetRadius = 3.5f;
        [Tooltip("Seconds after which it flies to the player from anywhere.")]
        [Min(1)] public float homeAfter = 12;
        [Min(1)] public float flySpeed = 16;
        [Min(0)] public float sparkleScale = .25f;
        [Header("Dropped (Leaky pocket)")]
        [Tooltip("A coin that fell out of the player's pocket: seconds before it can be picked up again.")]
        [Min(0)] public float droppedPickupDelay = 1.2f;
        [Tooltip("A dropped coin is picked up only this close (the player walks back for it); it never flies home by itself.")]
        [Min(0)] public float droppedPickupRadius = 1.4f;

        ExpeditionWorld world;
        float born;
        bool dropped;
        public bool Taken { get; private set; }
        public int Value => value;

        void Reset() => body = GetComponent<Rigidbody>();

        /// <summary>
        /// Fell out of the player's pocket (Leaky pocket): lies where it lands until the player walks back over it; it
        /// never flies to the bag by itself.
        /// </summary>
        public void Drop(ExpeditionWorld expedition, int worth, Vector3 velocity)
        {
            Launch(expedition, worth, velocity);
            dropped = true;
        }

        /// <summary>Thrown by a chest: sets what it is worth and sends it flying.</summary>
        public void Launch(ExpeditionWorld expedition, int worth, Vector3 velocity)
        {
            world = expedition;
            dropped = false;
            value = Mathf.Max(1, worth);
            born = Time.time;
            if (!body) body = GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 12;
            // The player walks through coins instead of kicking them about.
            if (world && world.motor)
                foreach (var own in GetComponentsInChildren<Collider>())
                    foreach (var player in world.motor.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(own, player);
        }

        void Update()
        {
            if (Taken || !world) return;
            if (!world.Running) return;
            float age = Time.time - born;
            if (dropped)
            {
                if (age >= droppedPickupDelay && InReach(droppedPickupRadius)) StartCoroutine(FlyIntoBag());
                return;
            }
            if (age < magnetDelay) return;
            if (age >= homeAfter || InReach(magnetRadius)) StartCoroutine(FlyIntoBag());
        }

        bool InReach(float radius)
        {
            if (!world.motor || !world.viewCamera) return false;
            Vector3 feet = world.motor.transform.position, eye = world.viewCamera.transform.position;
            Vector3 axis = eye - feet;
            float t = axis.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(transform.position - feet, axis) / axis.sqrMagnitude) : 0;
            return (transform.position - (feet + axis * t)).sqrMagnitude <= radius * radius;
        }

        /// <summary>Speeds up towards the bag (just below the view), following the player, then credits itself.</summary>
        IEnumerator FlyIntoBag()
        {
            Taken = true;
            // A small sparkle where it leaves the ground; the bag end stays clean (it is right in front of the eyes).
            world.Spawn(world.vfx ? world.vfx.pickupSparkle : null, transform.position, Quaternion.identity, colour, sparkleScale);
            if (body) body.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            float speed = 2, time = 0;
            while (true)
            {
                if (!world || !world.Running) { Destroy(gameObject); yield break; }
                var bag = world.viewCamera.transform;
                Vector3 target = bag.position + bag.forward * .4f - bag.up * .35f;
                time += Time.deltaTime;
                speed = Mathf.Min(flySpeed, speed + flySpeed * 3 * Time.deltaTime);
                // A small hop first, then straight in.
                Vector3 lift = Vector3.up * Mathf.Max(0, 1 - time * 4) * .6f * Time.deltaTime * 4;
                transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime) + lift;
                float distance = Vector3.Distance(transform.position, target);
                if (visual) visual.localScale = Vector3.one * Mathf.Lerp(.35f, 1, Mathf.Clamp01(distance / 1.5f));
                if (distance < .12f) break;
                yield return null;
            }
            world.CollectCoins(value, colour);
            Destroy(gameObject);
        }
    }
}
