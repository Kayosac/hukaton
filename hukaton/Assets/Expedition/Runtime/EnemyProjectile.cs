using System.Collections.Generic;
using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A projectile a creature shot (<see cref="WorldCreature.ranged"/>). Kinematic: it follows
    /// <see cref="EnemyProjectileFlight"/> exactly and sweeps a sphere for its first contact. It hits the player,
    /// allies and other creatures (never its shooter), breaks on the ground, rocks and trunks, pushes loose props, and
    /// a thrown card knocks it out of the air. The picture is a front view that always faces the camera.
    /// Spawned for the outing, so it disappears when the outing ends.
    /// </summary>
    public sealed class EnemyProjectile : MonoBehaviour
    {
        [Tooltip("Hand-drawn front view of the projectile; turned to the camera every frame.")]
        public SpriteRenderer picture;
        [Tooltip("Spin of the picture in the screen plane, degrees per second.")]
        public float spin;
        [Tooltip("Trails and glows: left behind to fade out when the projectile ends.")]
        public ParticleSystem[] trails = new ParticleSystem[0];
        [Min(0)] public float trailFadeSeconds = 1.2f;

        static readonly List<EnemyProjectile> active = new List<EnemyProjectile>();
        /// <summary>Projectiles in flight.</summary>
        public static IReadOnlyList<EnemyProjectile> Active => active;

        ExpeditionWorld world;
        EnemyProjectileProfile profile;
        WorldCreature shooter;
        ICombatant target;
        Vector3 velocity, launchPosition;
        float gravity, travelled, roll;
        bool ended;
        // Sticky hour card: the projectile's own clock runs slower inside the zone (same path, N times slower). 1 normally.
        TimedModifier timeScale;

        public EnemyProjectileProfile Profile => profile;
        public WorldCreature Shooter => shooter;
        public Vector3 Velocity => velocity;
        public bool Ended => ended;
        /// <summary>Flight-time multiplier (Sticky hour ×1/2…1/4); 1 normally. The path stays the same.</summary>
        public float TimeScale => timeScale.Current(Time.time, 1);
        /// <summary>Slows the flight for a while; refresh it every physics step from a zone.</summary>
        public void ApplyTimeScale(float scale, float seconds) => timeScale.Apply(Mathf.Clamp(scale, .05f, 10), seconds, Time.time);

        static readonly int SpriteUvRect = Shader.PropertyToID("_SpriteUVRect");
        static readonly int Anchor = Shader.PropertyToID("_Anchor");
        static readonly int Seed = Shader.PropertyToID("_Seed");

        void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        void OnDisable() => active.Remove(this);

        void OnDestroy()
        {
            active.Remove(this);
            if (world && world.sfx) world.sfx.StopLoop(this);
        }

        /// <summary>Sends it off. <paramref name="gravity"/> comes from <see cref="EnemyProjectileFlight.Launch"/>.</summary>
        public void Launch(ExpeditionWorld owner, EnemyProjectileProfile settings, WorldCreature from, ICombatant aimedAt, Vector3 initialVelocity, float shotGravity)
        {
            world = owner;
            profile = settings;
            shooter = from;
            target = aimedAt;
            velocity = initialVelocity;
            gravity = shotGravity;
            launchPosition = transform.position;
            travelled = 0;
            ended = false;
            roll = Random.Range(0, 360f);
            PreparePicture();
            UpdatePicture();
        }

        void PreparePicture()
        {
            if (!picture || !picture.sprite) return;
            // Same hand-drawn shader as the creatures: one full-rect cell, anchored in its middle.
            var sprite = picture.sprite;
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            var block = new MaterialPropertyBlock();
            picture.GetPropertyBlock(block);
            block.SetVector(SpriteUvRect, new Vector4(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height));
            block.SetFloat(Anchor, sprite.pivot.y / Mathf.Max(1, sprite.rect.height));
            block.SetFloat(Seed, Random.Range(0f, 100f));
            picture.SetPropertyBlock(block);
        }

        void FixedUpdate()
        {
            if (ended || !world || !profile) return;
            if (!world.Running) { End(); return; }
            float realDt = Time.fixedDeltaTime, dt = realDt * TimeScale;
            if (profile.homingTurnRate > 0 && Combat.IsAlive(target))
                velocity = EnemyProjectileFlight.Steer(velocity, target.Centre - transform.position, profile.homingTurnRate, dt);
            Vector3 from = transform.position, to = from, nextVelocity = velocity;
            EnemyProjectileFlight.Advance(ref to, ref nextVelocity, gravity, dt);

            if (profile.cardsShootDown && TouchesCard(from, to, realDt)) { ShootDown(); return; }
            if (Sweep(from, to, out var point, out var normal, out var collider, out var victim))
            {
                transform.position = point;
                Impact(point, normal, collider, victim);
                return;
            }
            transform.position = to;
            velocity = nextVelocity;
            // The trail streams out behind along the flight.
            if (velocity.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(velocity);
            travelled += Vector3.Distance(from, to);
            if (travelled > profile.maxDistance) Fade();
        }

        void Update()
        {
            if (ended || !world || !profile || !world.sfx || !profile.flightLoop) return;
            world.sfx.Loop(this, profile.flightLoop, transform.position);
        }

        void LateUpdate()
        {
            if (!ended) UpdatePicture();
        }

        void UpdatePicture()
        {
            if (!picture) return;
            roll += spin * Time.deltaTime;
            var viewer = world && world.viewCamera ? world.viewCamera : Camera.main;
            if (!viewer) return;
            // Faces the camera like the creatures' drawings: the art is a front view coming at the player.
            picture.transform.rotation = viewer.transform.rotation * Quaternion.Euler(0, 0, roll);
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>The first thing on this step: the world, a creature, an ally, or the player.</summary>
        bool Sweep(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal, out Collider collider, out ICombatant victim)
        {
            point = to; normal = Vector3.up; collider = null; victim = null;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-5f) return false;
            float nearest = float.MaxValue;
            int count = Physics.SphereCastNonAlloc(from, profile.radius, delta / length, hits, length, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.distance >= nearest) continue;
                if (shooter && hit.collider.transform.IsChildOf(shooter.transform)) continue;
                if (!EnemyProjectileFlight.Blocks(hit.collider)) continue;
                var fighter = Combat.Of(hit.collider);
                if (fighter is WorldCreature && !profile.hitsCreatures) continue;
                // Started inside it: count the hit where the step starts.
                bool inside = hit.distance <= 0 && hit.point == Vector3.zero;
                nearest = hit.distance;
                point = inside ? from : hit.point;
                normal = inside ? -delta / length : hit.normal;
                collider = hit.collider;
                victim = fighter != null && fighter.Alive ? fighter : null;
            }
            // The player rig is outside the physics mask: test its capsule directly.
            if (PlayerCapsule(out var bottom, out var top, out float playerRadius))
            {
                float distance = EnemyProjectileFlight.SegmentDistance(from, to, bottom, top, out float s, out float t);
                float along = s * length;
                if (distance <= playerRadius + profile.radius && along < nearest)
                {
                    nearest = along;
                    point = Vector3.Lerp(bottom, top, t) + (from + delta * s - Vector3.Lerp(bottom, top, t)).normalized * playerRadius;
                    normal = -delta / length;
                    collider = world.motor.characterController;
                    victim = world.Player;
                }
            }
            return nearest < float.MaxValue;
        }

        bool PlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            bottom = top = Vector3.zero; radius = 0;
            if (!world.motor || !world.motor.characterController || !Combat.IsAlive(world.Player)) return false;
            var body = world.motor.characterController;
            var t = body.transform;
            Vector3 scale = t.lossyScale;
            radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float half = Mathf.Max(0, body.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 centre = t.TransformPoint(body.center);
            bottom = centre - Vector3.up * half;
            top = centre + Vector3.up * half;
            return true;
        }

        bool TouchesCard(Vector3 from, Vector3 to, float dt)
        {
            var cards = CardProjectile.Active;
            float reach = profile.radius + (world.settings ? world.settings.cardRadius : .1f) + .12f;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!card || card.Resolved) continue;
                // Both move during the step: compare the two swept paths.
                Vector3 now = card.transform.position;
                Vector3 before = now - card.Velocity * dt;
                if (EnemyProjectileFlight.SegmentDistance(from, to, before, now, out _, out _) <= reach) return true;
            }
            return false;
        }

        static readonly List<ICombatant> nearby = new List<ICombatant>();

        void Impact(Vector3 point, Vector3 normal, Collider collider, ICombatant victim)
        {
            Vector3 flight = velocity.sqrMagnitude > 1e-4f ? velocity.normalized : -normal;
            if (victim != null) Hit(victim, point, flight);
            else if (collider && collider.attachedRigidbody && !collider.attachedRigidbody.isKinematic && Combat.Of(collider) == null)
                CardPhysics.Push(collider.attachedRigidbody, flight * profile.knockback);
            if (profile.splashRadius > 0)
            {
                nearby.Clear();
                world.Combatants(nearby);
                foreach (var fighter in nearby)
                {
                    if (ReferenceEquals(fighter, victim) || ReferenceEquals(fighter, shooter) || !fighter.Alive) continue;
                    if (fighter is WorldCreature && !profile.hitsCreatures) continue;
                    if (Vector3.Distance(fighter.Centre, point) > profile.splashRadius && Vector3.Distance(fighter.Position, point) > profile.splashRadius) continue;
                    Vector3 away = fighter.Position - point; away.y = 0;
                    Hit(fighter, point, away.sqrMagnitude > .01f ? away.normalized : flight);
                }
            }
            world.Spawn(profile.impact, point + normal * .05f, Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up), profile.impactTint, profile.effectScale);
            world.Play(profile.impactCue, point);
            End();
        }

        void Hit(ICombatant victim, Vector3 point, Vector3 direction)
        {
            direction.y = 0;
            direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.zero;
            Vector3 push = direction * profile.knockback + Vector3.up * profile.knockbackUp;
            Vector3 source = shooter ? shooter.Centre : launchPosition;
            // A shooter shrunk by the Crooked mirror hits weaker (never below ½ heart).
            int damage = shooter ? WorldCreature.ScaledDamage(profile.damage, shooter.DamageScale) : profile.damage;
            bool landed = victim.TakeDamage(new DamageInfo(damage, push, point, source, profile.kind, shooter));
            if (!landed && damage > 0) return;
            ApplyEffects(victim, point);
        }

        void ApplyEffects(ICombatant victim, Vector3 point)
        {
            if (!profile.HasEffects) return;
            bool slow = profile.slowSeconds > 0 && profile.slowScale < 1;
            if (victim is WorldCreature creature)
            {
                if (!creature.Alive) return;
                if (slow) creature.ApplyMoveSpeedScale(profile.slowScale, profile.slowSeconds);
                if (profile.freezeSeconds > 0) creature.Freeze(profile.freezeSeconds);
                if (profile.stunSeconds > 0) creature.Stagger(profile.stunSeconds, point);
            }
            else if (ReferenceEquals(victim, world.Player) && world.motor)
            {
                // Frozen in place reads as the strongest slowdown; whichever is stronger wins while both last.
                if (profile.freezeSeconds > 0) world.motor.ApplyModifier(MotorModifier.MoveSpeed, 0, profile.freezeSeconds);
                else if (slow) world.motor.ApplyModifier(MotorModifier.MoveSpeed, profile.slowScale, profile.slowSeconds);
            }
        }

        /// <summary>Knocked out of the air by a thrown card.</summary>
        public void ShootDown()
        {
            if (ended) return;
            world.Spawn(profile.shotDown ? profile.shotDown : profile.impact, transform.position, Quaternion.identity, profile.impactTint, profile.effectScale);
            world.Play(profile.shotDownCue ? profile.shotDownCue : profile.impactCue, transform.position);
            End();
        }

        /// <summary>Out of range: it just puffs away.</summary>
        void Fade()
        {
            world.Spawn(profile.shotDown, transform.position, Quaternion.identity, profile.impactTint, profile.effectScale * .6f);
            End();
        }

        void End()
        {
            if (ended) return;
            ended = true;
            if (world && world.sfx) world.sfx.StopLoop(this);
            // The trail stays where it was and fades out instead of vanishing with the projectile.
            foreach (var trail in trails)
            {
                if (!trail) continue;
                trail.transform.SetParent(world ? world.spawnedObjects : null, true);
                trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(trail.gameObject, trailFadeSeconds);
            }
            Destroy(gameObject);
        }

        /// <summary>Removes every projectile in flight at once (the debug sandbox reset).</summary>
        public static void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i]) active[i].End();
            active.Clear();
        }
    }
}
