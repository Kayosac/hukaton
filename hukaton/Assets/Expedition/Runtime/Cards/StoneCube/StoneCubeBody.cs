using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The stone cube made by «Каменный куб»: a real (non-kinematic) Rigidbody of mass μ that Impulse, Attraction,
    /// creatures and the player's rope move about; a step, cover and obstacle until the end of the outing.
    /// The root stays unscaled so whatever rides on it (a mushroom cap, a rope's anchor) keeps its own size: the side
    /// is applied to the box collider and to the crooked stone visual (authored for a side of 1 m). The top of the box
    /// is a level surface. Registered as a <see cref="CardZoneKind.StoneCube"/> zone so other cards can find it.
    /// </summary>
    public sealed class StoneCubeBody : MonoBehaviour, IOutingSpawned, ICardZone
    {
        [Header("Authored parts")]
        public Rigidbody body;
        public BoxCollider box;
        [Tooltip("Crooked stone mesh authored for a 1 m cube, centred on the root.")]
        public Transform visual;
        [Header("Appearance")]
        [Tooltip("The stone pops out of the ground: scale-in time of the visual (the collider is full size at once).")]
        [Min(0)] public float appearSeconds = .22f;
        [Tooltip("Overshoot of the pop.")]
        [Min(0)] public float appearOvershoot = .12f;

        public float Side { get; private set; } = 1;
        public ExpeditionWorld World { get; private set; }
        public CardZoneKind Kind => CardZoneKind.StoneCube;
        public Bounds Bounds => box ? box.bounds : new Bounds(transform.position, Vector3.one * Side);
        public bool Contains(Vector3 point) => box && (box.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;

        float age = float.PositiveInfinity;

        /// <summary>Applies the card's numbers: side in metres and mass (board μ, the same units as Rigidbody.mass).</summary>
        public void Configure(float side, float mass)
        {
            Side = Mathf.Max(.1f, side);
            if (box) { box.size = Vector3.one * Side; box.center = Vector3.zero; }
            if (body)
            {
                body.mass = Mathf.Max(.01f, mass);
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
            age = 0;
            ApplyVisualScale(appearSeconds > 0 ? .55f : 1);
        }

        public void OnSpawned(ExpeditionWorld world)
        {
            World = world;
            if (world) world.Zones.Add(this);
        }

        void OnDisable()
        {
            if (World) World.Zones.Remove(this);
        }

        void Update()
        {
            if (age > appearSeconds) return;
            age += Time.deltaTime;
            float t = appearSeconds > 0 ? Mathf.Clamp01(age / appearSeconds) : 1;
            // Out-back: grows past its size and settles.
            float s = 1 + appearOvershoot * Mathf.Sin(t * Mathf.PI) - (1 - t) * (1 - t) * .45f;
            ApplyVisualScale(t >= 1 ? 1 : s);
        }

        void ApplyVisualScale(float factor)
        {
            if (visual) visual.localScale = Vector3.one * (Side * factor);
        }
    }
}
