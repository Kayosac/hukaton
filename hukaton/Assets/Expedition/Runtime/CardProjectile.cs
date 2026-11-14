using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A thrown card. Kinematic: it follows <see cref="CardFlight"/> exactly (same as the preview) and sweeps a
    /// small sphere for its first contact. Visually it leaves the hand pose, flattens out and spins like a
    /// real thrown playing card, with a slight wobble.
    /// </summary>
    public sealed class CardProjectile : MonoBehaviour
    {
        public CardView view;
        public TrailRenderer trail;
        [Tooltip("Spin around the card's own normal, degrees per second.")]
        public float spinSpeed = 1600;
        [Min(0)] public float handBlendSeconds = .1f;
        [Min(.01f)] public float worldCardWidth = .24f;

        ExpeditionWorld world;
        CardCopy card;
        CardDefinition definition;
        Vector3 velocity;
        float age, spinAngle, twirl;
        Vector3 launchPosition;
        bool resolved;
        Pose? handPose;
        ICardFlightPath path;
        CardVolley volley;
        float loopVolume = 1;

        static readonly System.Collections.Generic.List<CardProjectile> active = new System.Collections.Generic.List<CardProjectile>();
        /// <summary>Cards in flight (creatures' projectiles check them: a card knocks a projectile out of the air).</summary>
        public static System.Collections.Generic.IReadOnlyList<CardProjectile> Active => active;

        void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        void OnDisable() => active.Remove(this);

        public bool Resolved => resolved;
        public CardCopy Card => card;
        public Vector3 Velocity => velocity;
        public ExpeditionWorld World => world;
        public CardDefinition Definition => definition;
        public Vector3 LaunchPosition => launchPosition;
        /// <summary>The card's own flight (<see cref="ICustomFlight"/>: Бумеранг), or null for the ordinary ballistic one.</summary>
        public ICardFlightPath Path => path;
        /// <summary>The cards thrown together with this one (<see cref="ICardVolley"/>: Веер), or null for a single card.</summary>
        public CardVolley Volley => volley;

        /// <summary>
        /// Joins the volley of one throw (Веер): the copy's attempt settles once, after the last card of it.
        /// <paramref name="flightLoopVolume"/> scales its flutter (0: it flies silent, the volley's other cards flutter).
        /// </summary>
        internal void JoinVolley(CardVolley shared, float flightLoopVolume)
        {
            volley = shared;
            loopVolume = Mathf.Max(0, flightLoopVolume);
            shared?.Add(this);
        }

        public void Launch(ExpeditionWorld owner, CardCopy copy, CardView source, Vector3 initialVelocity, Pose? fromHand)
        {
            world = owner;
            card = copy;
            definition = copy.IsReturn ? null : owner.workshop.config.Find(copy.Spec.Id);
            velocity = initialVelocity;
            launchPosition = transform.position;
            handPose = fromHand;
            spinAngle = Random.Range(0, 360f);
            // Each throw twirls one way or the other.
            twirl = Random.value < .5f ? -1 : 1;
            if (source) view.CopyFrom(source);
            else view.Show(owner.Artwork(copy), owner.hand ? owner.hand.cardBack : null, copy.Level);
            Color accent = owner.Accent(copy);
            if (trail)
            {
                trail.Clear();
                trail.startColor = new Color(accent.r, accent.g, accent.b, .8f);
                trail.endColor = new Color(accent.r, accent.g, accent.b, 0);
            }
            path = definition && definition.effectAsset is ICustomFlight custom
                ? custom.BeginFlight(new CardFlightLaunch(owner, copy, definition, launchPosition, initialVelocity))
                : null;
            UpdateVisual(0);
        }

        void FixedUpdate()
        {
            if (resolved || !world) return;
            if (!world.Running) { Destroy(gameObject); return; }
            if (path != null) { path.Step(this, Time.fixedDeltaTime); return; }
            Vector3 position = transform.position, next = position, nextVelocity = velocity;
            CardFlight.Advance(ref next, ref nextVelocity, world.settings, Time.fixedDeltaTime);
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
            if (!resolved && age > world.settings.projectileLife) { if (path != null) Drop(); else Fizzle(); }
            var loop = path?.FlightLoop ?? (world.cues ? world.cues.cardFlutter : null);
            if (!resolved && loopVolume > 0) world.sfx?.Loop(this, loop, transform.position, Mathf.Clamp01(velocity.magnitude / 18f) * loopVolume);
        }

        void LateUpdate() { if (!resolved) UpdateVisual(Time.deltaTime); }

        void UpdateVisual(float dt)
        {
            spinAngle += spinSpeed * (path != null ? path.SpinMultiplier : 1) * dt * twirl;
            Vector3 direction = velocity.sqrMagnitude > .001f ? velocity.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < .001f) side = Vector3.right;
            Vector3 up = Vector3.Cross(direction, side.normalized);
            // A slight wobble around the flight direction; otherwise the card flies flat.
            up = Quaternion.AngleAxis(Mathf.Sin(age * 23) * 4, direction) * up;
            // The quad's normal (local Z) points along that up axis: the card flies flat, spinning in its plane.
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

        /// <summary>Moves the card along its own flight (<see cref="ICardFlightPath"/>).</summary>
        public void MoveTo(Vector3 position, Vector3 newVelocity)
        {
            transform.position = position;
            velocity = newVelocity;
        }

        public void Resolve(Collider target, Vector3 point, Vector3 normal)
        {
            if (resolved || !world) return;
            // A card with its own flight never sticks: it strikes the obstacle and drops (Бумеранг into a wall).
            if (path != null) { Bounce(target, point, normal); return; }
            resolved = true;
            world.sfx?.StopLoop(this);
            if (world.Running) world.ApplyImpact(card, definition, target, point, normal, velocity, launchPosition);
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

        /// <summary>
        /// The card struck something solid on its own flight: the impact is applied, the card bounces off and drops,
        /// and its attempt is over (held cards settle in <see cref="OnDestroy"/>).
        /// </summary>
        public void Bounce(Collider target, Vector3 point, Vector3 normal)
        {
            if (resolved || !world) return;
            resolved = true;
            world.sfx?.StopLoop(this);
            transform.position = point;
            if (world.Running) world.ApplyImpact(card, definition, target, point, normal, velocity, launchPosition);
            Vector3 away = Vector3.Reflect(velocity, normal.sqrMagnitude > .001f ? normal.normalized : -velocity.normalized);
            FallAway(point + normal * .05f, away * .3f + Vector3.up * 1.5f);
        }

        /// <summary>The card's own flight ran out of time: it loses its lift and drops where it is.</summary>
        public void Drop()
        {
            if (resolved || !world) return;
            resolved = true;
            world.sfx?.StopLoop(this);
            FallAway(transform.position, velocity * .25f + Vector3.up * .5f);
        }

        /// <summary>
        /// The card came back and was caught (Бумеранг): the same copy returns to the hand with the use its throw took
        /// (<see cref="Expedition.Catch"/>); nothing is left in the world.
        /// </summary>
        public bool Catch(SfxCue sound)
        {
            if (resolved || !world) return false;
            resolved = true;
            world.sfx?.StopLoop(this);
            bool back = card != null && world.workshop && world.workshop.Model?.Current != null && world.workshop.Model.Current.Catch(card.Id);
            world.Play(sound, transform.position);
            if (world.feel) world.feel.Kick(new Vector3(4, 0, -3));
            Destroy(gameObject);
            return back;
        }

        void FallAway(Vector3 position, Vector3 fallVelocity)
        {
            world.SpawnDebris(view, position, view.transform.rotation, fallVelocity,
                new Vector3(Random.Range(-500, 500), Random.Range(-200, 200), Random.Range(-700, 700)), .9f);
            Destroy(gameObject);
        }

        void Fizzle()
        {
            resolved = true;
            world.sfx?.StopLoop(this);
            world.Spawn(world.vfx ? world.vfx.paperPoof : null, transform.position, Quaternion.identity, world.Accent(card), .6f);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            active.Remove(this);
            if (!world) return;
            if (world.sfx) world.sfx.StopLoop(this);
            // The attempt is over (hit, miss or gone): a card with uses left (Teleport II / III) returns to the hand;
            // one that could only come back by being caught (Бумеранг) and was not goes to the spent pile.
            // A volley (Веер) settles its one copy once, after its last card.
            if (card != null && world.workshop && world.workshop.Model != null && (volley == null || volley.Finish(this)))
                world.workshop.Model.Current?.Settle(card.Id);
        }
    }
}
