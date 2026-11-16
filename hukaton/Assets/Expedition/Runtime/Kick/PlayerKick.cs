using System;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The kick (board «Пинок и трофейные карты»): F on an outing and in the cave. The player always has it, cards or no
    /// cards, as often as its own cooldown allows (<see cref="cooldown"/>, 3 s for a start; the ring round the crosshair
    /// shows it, <see cref="KickCooldownRing"/>). It is for control, not for killing. The blow lands at once: the nearest
    /// thing in front of the eyes within reach — a creature (<see cref="WorldCreature.Kicked"/>: one point of damage, a
    /// shove and a stun by its toughness), a chest mimic (<see cref="ChestMimic.Kicked"/>: shut, it wakes and bites, it
    /// never dies of it), a loose thing (crate, pot, barrel, the stone cube, a chest — shoved, broken by the thresholds
    /// of its kind) or a fixed thing that breaks (a fence). Every hit has its punch: a hit-stop, a jolt of the view, an
    /// impact burst and a thump. The leg on screen is <see cref="KickView"/>'s.
    /// </summary>
    public sealed class PlayerKick : MonoBehaviour
    {
        public ExpeditionWorld world;
        [Tooltip("The leg on screen.")]
        public KickView view;

        [Header("Input and cooldown")]
        public Key key = Key.F;
        [Tooltip("Seconds from one kick to the next (3 for a start; an upgrade may shorten it later).")]
        [Min(0)] public float cooldown = 3;

        [Header("The blow")]
        [Tooltip("The blow lands this long after the press: as soon as the leg is out.")]
        [Min(0)] public float hitDelay = .045f;
        [Tooltip("Reach from the eyes, metres: about a goblin dog's bite — to kick, you have to come close.")]
        [Min(.2f)] public float reach = 1.8f;
        [Tooltip("Thickness of the swept leg, metres.")]
        [Min(.05f)] public float radius = .45f;
        [Tooltip("Shove given to a small creature, m/s (bigger ones take less, see KickRules).")]
        [Min(0)] public float creatureShove = 6;
        [Tooltip("Blow on a loose thing, m/s, and how far it flies for that (by its weight, as a card's blow).")]
        [Min(0)] public float propSpeed = 6;
        [Min(1)] public float propFling = 1.6f;
        [Tooltip("Upward tilt of the shove.")]
        [Range(0, 1)] public float lift = .25f;
        [Tooltip("Layers the leg can meet.")]
        public LayerMask layers = ~0;

        [Header("Feel")]
        [Tooltip("The view dips into the kick as it starts (degrees per second, x down).")]
        public Vector3 swingKick = new Vector3(45, -10, 0);
        [Tooltip("A hit: time stops this long (real seconds), the view jolts back and shakes, the field of view punches.")]
        [Min(0)] public float hitStop = .065f;
        public Vector3 hitKick = new Vector3(-110, 25, 0);
        [Range(0, 1)] public float hitTrauma = .28f;
        public float hitFovPunch = -45;
        public SfxCue swingSound, hitSound;
        [Tooltip("The impact where the boot lands (Retro Arsenal) and a little dust with it.")]
        public VfxBurst hitBurst;
        [Min(.01f)] public float hitBurstScale = .28f;
        public VfxBurst hitDust;
        [Min(.01f)] public float hitDustScale = .15f;

        /// <summary>A kick was made (landed or not): the cave's instruction card waits for it.</summary>
        public event Action Kicked;
        /// <summary>A kick landed on something.</summary>
        public event Action<Collider> Landed;

        public int KickCount { get; private set; }
        /// <summary>The leg is on its way: the blow has not landed yet.</summary>
        public bool Swinging => hitAt > 0;
        /// <summary>Seconds left until the next kick.</summary>
        public float ReadyIn => Mathf.Max(0, readyAt - Time.time);
        /// <summary>1 right after a kick, 0 once the next one is allowed.</summary>
        public float CooldownLeft01 => cooldown <= 0 ? 0 : Mathf.Clamp01(ReadyIn / cooldown);

        float readyAt, hitAt;
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        bool Usable => world && world.Running && world.control && world.control.GameplayActive;

        void Start()
        {
            if (view) view.Bind(world);
            if (world) world.OutingBegan += ResetCooldown;
        }

        void OnDestroy()
        {
            if (world) world.OutingBegan -= ResetCooldown;
        }

        void OnDisable() => hitAt = 0;

        void Update()
        {
            if (hitAt > 0 && Time.time >= hitAt)
            {
                hitAt = 0;
                Strike();
            }
            if (!Usable)
            {
                hitAt = 0;
                return;
            }
            if (world.control.CapturedThisFrame) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[key].wasPressedThisFrame) return;
            TryKick();
        }

        /// <summary>Starts a kick if one is allowed now (also for tests and the cheat menu). False while cooling down.</summary>
        public bool TryKick()
        {
            if (!Usable || Time.time < readyAt || hitAt > 0) return false;
            readyAt = Time.time + cooldown;
            hitAt = Time.time + hitDelay;
            KickCount++;
            if (view) view.Play();
            if (world.feel) world.feel.Kick(swingKick);
            world.Play(swingSound);
            Kicked?.Invoke();
            return true;
        }

        /// <summary>The next kick is allowed at once (a new outing, the cheat menu).</summary>
        public void ResetCooldown() => readyAt = 0;

        void Strike()
        {
            if (!world || !world.Running || !world.viewCamera) return;
            var eye = world.viewCamera.transform;
            var direction = eye.forward;
            if (!Find(eye.position, direction, out var hit)) return;
            if (!Apply(hit.collider, hit.point, direction)) return;
            if (world.time && hitStop > 0) world.time.HitStop(hitStop);
            if (world.feel)
            {
                world.feel.Kick(hitKick);
                world.feel.AddTrauma(hitTrauma);
                world.feel.PunchFov(hitFovPunch);
            }
            world.Play(hitSound, hit.point);
            var facing = Quaternion.LookRotation(-direction);
            world.Spawn(hitBurst, hit.point, facing, Color.white, hitBurstScale);
            world.Spawn(hitDust, hit.point, facing, Color.white, hitDustScale);
            if (view) view.Landed();
            Landed?.Invoke(hit.collider);
        }

        /// <summary>The nearest thing the leg meets in front of the eyes (never the player himself, his allies or triggers).</summary>
        bool Find(Vector3 origin, Vector3 direction, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, Hits, reach, layers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            var player = world.motor ? world.motor.transform : null;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                Hits[i] = default;
                var collider = hit.collider;
                if (!collider || player && collider.transform.IsChildOf(player)) continue;
                var fighter = Combat.Of(collider);
                if (fighter != null && fighter.Faction == Faction.Player) continue;
                // Started inside it: a sweep reports no point, take the closest one on the collider.
                var point = hit.distance <= 0 && hit.point == Vector3.zero ? collider.ClosestPoint(origin) : hit.point;
                float distance = hit.distance <= 0 ? Vector3.Distance(origin, point) : hit.distance;
                if (distance >= nearest || !Kickable(collider)) continue;
                nearest = distance;
                best = hit;
                best.point = point;
            }
            return nearest < float.MaxValue;
        }

        static bool Kickable(Collider collider)
        {
            var creature = collider.GetComponentInParent<WorldCreature>();
            if (creature) return creature.Alive;
            if (collider.GetComponentInParent<ChestMimic>() is ChestMimic mimic) return mimic.Alive || PropPhysics.IsLoose(collider.attachedRigidbody);
            if (collider.attachedRigidbody) return PropPhysics.IsLoose(collider.attachedRigidbody);
            return PropPhysics.Fixed(collider);
        }

        /// <summary>What the boot does to what it met. True when it did something (feedback plays).</summary>
        bool Apply(Collider collider, Vector3 point, Vector3 direction)
        {
            var creature = collider.GetComponentInParent<WorldCreature>();
            if (creature) return creature.Kicked(point, direction, creatureShove, lift);
            var mimic = collider.GetComponentInParent<ChestMimic>();
            if (mimic && mimic.Alive && mimic.Kicked(point, direction)) return true;
            var body = collider.attachedRigidbody;
            if (body) return PropPhysics.Nudge(body, point, (direction + Vector3.up * lift).normalized, propSpeed, propFling, lift, 4);
            return PropPhysics.HitFixed(collider, point, direction, propSpeed);
        }
    }
}
