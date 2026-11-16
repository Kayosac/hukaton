using System;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The kick (board «Пинок и трофейные карты»): F on an outing and in the cave. The player always has it, cards or no
    /// cards; it is for control, not for killing. It costs stamina and has a short cooldown; a worn-out player only
    /// twitches his leg. The blow lands a beat after the press (<see cref="hitDelay"/>) on the nearest thing in front of the
    /// eyes within reach: a creature (<see cref="WorldCreature.Kicked"/>: one point of damage, a shove and a stun by its
    /// toughness), a chest mimic (<see cref="ChestMimic.Kicked"/>: shut, it wakes and bites, it never dies of it), a loose
    /// thing (a crate, a pot, a barrel, the stone cube, a chest — shoved, broken by the thresholds of its kind) or a fixed
    /// thing that breaks (a fence). The rest — the shove into a bog, off a cliff, a frozen shell shattering — the world
    /// already does. The leg on screen and the feel are <see cref="KickView"/>'s.
    /// </summary>
    public sealed class PlayerKick : MonoBehaviour
    {
        public ExpeditionWorld world;
        [Tooltip("The leg on screen.")]
        public KickView view;

        [Header("Input and cost")]
        public Key key = Key.F;
        [Tooltip("Seconds from one kick to the next.")]
        [Min(0)] public float cooldown = .8f;
        [Tooltip("Stamina a kick costs (a jump costs 12 of 100).")]
        [Min(0)] public float staminaCost = 15;

        [Header("The blow")]
        [Tooltip("The blow lands this long after the press: the leg's swing.")]
        [Min(0)] public float hitDelay = .12f;
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
        [Tooltip("A hit stops time this long (seconds of real time).")]
        [Min(0)] public float hitStop = .05f;
        [Tooltip("The view nods into the kick (degrees per second, x down) and shakes on a hit.")]
        public Vector3 swingKick = new Vector3(30, 0, 0);
        public Vector3 hitKick = new Vector3(-40, 0, 0);
        [Range(0, 1)] public float hitTrauma = .18f;
        public SfxCue swingSound, hitSound, tiredSound;
        [Tooltip("Dust where the boot lands (Retro Arsenal).")]
        public VfxBurst hitDust;
        [Min(.01f)] public float hitDustScale = .28f;

        /// <summary>A kick was made (landed or not): the cave's instruction card waits for it.</summary>
        public event Action Kicked;
        /// <summary>A kick landed on something.</summary>
        public event Action<Collider> Landed;

        public int KickCount { get; private set; }
        public bool Swinging => hitAt > 0;
        public float ReadyIn => Mathf.Max(0, readyAt - Time.time);

        float readyAt, hitAt;
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        bool Usable => world && world.Running && world.control && world.control.GameplayActive;

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

        /// <summary>Starts a kick if one is allowed now (also for tests and the cheat menu). False: cooling down, too tired.</summary>
        public bool TryKick()
        {
            if (!Usable || Time.time < readyAt || hitAt > 0) return false;
            var stamina = world.motor ? world.motor.stamina : null;
            if (stamina && !stamina.TrySpend(staminaCost))
            {
                readyAt = Time.time + cooldown * .5f;
                if (view) view.Tired();
                world.Play(tiredSound);
                return false;
            }
            readyAt = Time.time + cooldown;
            hitAt = Time.time + hitDelay;
            KickCount++;
            if (view) view.Play();
            if (world.feel) world.feel.Kick(swingKick);
            world.Play(swingSound);
            Kicked?.Invoke();
            return true;
        }

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
            }
            world.Play(hitSound, hit.point);
            world.Spawn(hitDust, hit.point, Quaternion.LookRotation(-direction), Color.white, hitDustScale);
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
