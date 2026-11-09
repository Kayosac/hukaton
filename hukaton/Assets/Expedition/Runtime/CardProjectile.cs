using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A thrown card. Kinematic: it follows <see cref="CardFlight"/> exactly (same as the preview) and sweeps a
    /// small sphere for its first contact. Visually it leaves the hand pose, flattens out and spins like a
    /// real thrown playing card, banking with its curve.
    /// </summary>
    public sealed class CardProjectile : MonoBehaviour
    {
        public CardView view;
        public TrailRenderer trail;
        [Tooltip("Spin around the card's own normal, degrees per second.")]
        public float spinSpeed = 1600;
        [Tooltip("Bank angle at full curve, degrees.")]
        public float bankAngle = 35;
        [Min(0)] public float handBlendSeconds = .1f;
        [Min(.01f)] public float worldCardWidth = .24f;

        ExpeditionWorld world;
        CardCopy card;
        CardDefinition definition;
        Vector3 velocity;
        Vector2 spin;
        float age, spinAngle;
        bool resolved;
        Pose? handPose;

        public bool Resolved => resolved;
        public CardCopy Card => card;
        public Vector3 Velocity => velocity;

        public void Launch(ExpeditionWorld owner, CardCopy copy, CardView source, Vector3 initialVelocity, Vector2 curvature, Pose? fromHand)
        {
            world = owner;
            card = copy;
            definition = copy.IsReturn ? null : owner.workshop.config.Find(copy.Spec.Id);
            velocity = initialVelocity;
            spin = curvature;
            handPose = fromHand;
            spinAngle = Random.Range(0, 360f);
            if (source) view.CopyFrom(source);
            else view.Show(owner.Artwork(copy), owner.hand ? owner.hand.cardBack : null);
            Color accent = owner.Accent(copy);
            if (trail)
            {
                trail.Clear();
                trail.startColor = new Color(accent.r, accent.g, accent.b, .8f);
                trail.endColor = new Color(accent.r, accent.g, accent.b, 0);
            }
            UpdateVisual(0);
        }

        void FixedUpdate()
        {
            if (resolved || !world) return;
            if (!world.Running) { Destroy(gameObject); return; }
            Vector3 position = transform.position, next = position, nextVelocity = velocity;
            CardFlight.Advance(ref next, ref nextVelocity, spin, world.settings, Time.fixedDeltaTime);
            if (CardFlight.Sweep(position, next, world.settings.cardRadius, out var hit))
            {
                transform.position = hit.point;
                Resolve(hit.collider, hit.point, hit.normal);
                return;
            }
            transform.position = next;
            velocity = nextVelocity;
        }

        void Update()
        {
            if (!world) return;
            age += Time.deltaTime;
            if (!resolved && age > world.settings.projectileLife) Fizzle();
            if (!resolved) world.sfx?.Loop(this, world.cues ? world.cues.cardFlutter : null, transform.position, Mathf.Clamp01(velocity.magnitude / 18f));
        }

        void LateUpdate() { if (!resolved) UpdateVisual(Time.deltaTime); }

        void UpdateVisual(float dt)
        {
            spinAngle += spinSpeed * dt * (spin.x >= 0 ? 1 : -1);
            Vector3 direction = velocity.sqrMagnitude > .001f ? velocity.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < .001f) side = Vector3.right;
            Vector3 up = Vector3.Cross(direction, side.normalized);
            // Banks into a sideways curve; up/down spin keeps the card flat.
            up = Quaternion.AngleAxis(-spin.x * bankAngle + Mathf.Sin(age * 23) * 4, direction) * up;
            // The quad's normal (local Z) points along the banked up axis: the card flies flat, spinning in its plane.
            Quaternion flat = Quaternion.LookRotation(up, direction) * Quaternion.Euler(0, 0, spinAngle);
            Vector3 position = transform.position;
            Quaternion rotation = flat;
            float size = worldCardWidth;
            if (handPose.HasValue && age < handBlendSeconds)
            {
                float k = Ease.OutCubic(age / handBlendSeconds);
                position = Vector3.Lerp(handPose.Value.position, position, k);
                rotation = Quaternion.Slerp(handPose.Value.rotation, flat, k);
                size = Mathf.Lerp(world.hand ? world.hand.cardWidth : size, worldCardWidth, k);
            }
            view.transform.SetPositionAndRotation(position, rotation);
            view.transform.localScale = new Vector3(size, size * 1.5f, 1);
        }

        public void Resolve(Collider target, Vector3 point, Vector3 normal)
        {
            if (resolved || !world) return;
            resolved = true;
            world.sfx?.StopLoop(this);
            if (world.Running) world.ApplyImpact(card, definition, target, point, normal, velocity);
            // The card itself sticks where it hit, edge first, quivers, then burns away in its element's colour.
            Vector3 direction = velocity.sqrMagnitude > .001f ? velocity.normalized : -normal;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < .001f) side = Vector3.right;
            Vector3 cardNormal = Vector3.Cross(side.normalized, direction);
            Quaternion embedded = Quaternion.AngleAxis(Random.Range(-40f, 40f), direction) * Quaternion.LookRotation(cardNormal, direction);
            float halfLength = worldCardWidth * .75f;
            world.SpawnStuckCard(view, point - direction * (halfLength - .05f), embedded, target ? target.transform : null, world.Accent(card));
            Destroy(gameObject);
        }

        void Fizzle()
        {
            resolved = true;
            world.sfx?.StopLoop(this);
            world.Spawn(world.vfx ? world.vfx.paperPoof : null, transform.position, Quaternion.identity, world.Accent(card), .6f);
            Destroy(gameObject);
        }

        void OnDestroy() { if (world && world.sfx) world.sfx.StopLoop(this); }
    }
}
