using System;
using UnityEngine;
using Hakaton.Character;

namespace Hakaton.Expeditions
{
    /// <summary>How a creature that grabs (swamp mutant) holds the player.</summary>
    [Serializable]
    public sealed class GrabSettings
    {
        [Tooltip("Its strike grabs the player: no knock-back, the player cannot walk while held and is squeezed. " +
                 "The hold breaks when it is hurt (a card), frozen or knocked down, or when the player gets away (a jump, a push).")]
        public bool enabled;
        [Tooltip("How long it holds, seconds.")]
        [Min(.1f)] public float holdSeconds = 1.5f;
        [Tooltip("Damage of every squeeze while it holds: ½ heart per unit.")]
        [Min(0)] public int squeezeHalfHearts = 1;
        [Tooltip("Seconds between squeezes.")]
        [Min(.1f)] public float squeezeInterval = .75f;
        [Tooltip("Sucks blood: every squeeze that lands heals it by this much health (the leech). 0: none.")]
        [Min(0)] public int drainHeal;
        [Tooltip("The player slips out once further than the strike's contact range plus this, metres.")]
        [Min(0)] public float slip = .8f;

        [Header("On the player's screen")]
        [Tooltip("While it holds the player this picture (a child of the prefab, off by default) snaps over his view up close and " +
                 "shakes as if it were strangling him; its drawing in the world is hidden meanwhile. Empty: none.")]
        public SpriteRenderer screenPicture;
        [Tooltip("Share of the view's height the picture covers.")]
        [Min(.1f)] public float screenFill = 1.1f;
        [Tooltip("How far in front of the eyes it hangs, metres.")]
        [Min(.05f)] public float screenDistance = .5f;
        [Tooltip("The strangling shake: how far it jerks (share of the view) and how fast.")]
        [Min(0)] public float shake = .025f;
        [Min(0)] public float shakeRate = 18;
        [Tooltip("It snaps in this fast, seconds.")]
        [Min(.01f)] public float snapSeconds = .1f;
    }

    public sealed partial class WorldCreature
    {
        /// <summary>
        /// The grip up close: while it holds the player its picture covers his view in front of the camera, jerking and
        /// shaking (harder at every squeeze); its drawing in the world is hidden. It snaps in and drops away quickly.
        /// </summary>
        void UpdateGripPicture(float dt)
        {
            var picture = grab.screenPicture;
            if (!picture || !world) return;
            var eye = world.viewCamera;
            bool on = holding && Alive && !dying && eye && ReferenceEquals(Target, world.Player);
            gripShown = Mathf.MoveTowards(gripShown, on ? 1 : 0, dt / grab.snapSeconds);
            gripJolt = Mathf.MoveTowards(gripJolt, 0, dt * 4);
            bool visible = gripShown > .001f && eye;
            if (picture.gameObject.activeSelf != visible) picture.gameObject.SetActive(visible);
            if (view && view.spriteRenderer) view.spriteRenderer.enabled = !visible;
            if (!visible || !picture.sprite) return;
            if (gripBlock == null)
            {
                // The hand-drawn shader of the creatures: one full cell, anchored in its middle.
                gripBlock = new MaterialPropertyBlock();
                var sprite = picture.sprite;
                Rect rect = sprite.textureRect;
                picture.GetPropertyBlock(gripBlock);
                gripBlock.SetVector("_SpriteUVRect", new Vector4(rect.x / sprite.texture.width, rect.y / sprite.texture.height,
                    rect.width / sprite.texture.width, rect.height / sprite.texture.height));
                gripBlock.SetFloat("_Anchor", .5f);
                gripBlock.SetFloat("_Seed", UnityEngine.Random.Range(0f, 100f));
                picture.SetPropertyBlock(gripBlock);
            }
            var cam = eye.transform;
            float d = grab.screenDistance;
            float viewHeight = 2 * d * Mathf.Tan(eye.fieldOfView * .5f * Mathf.Deg2Rad);
            float ease = 1 - (1 - gripShown) * (1 - gripShown);
            float size = viewHeight * grab.screenFill / Mathf.Max(.001f, picture.sprite.bounds.size.y);
            float t = Time.time * grab.shakeRate;
            float amount = grab.shake * viewHeight * (1 + 3 * gripJolt);
            Vector3 jerk = cam.right * (Mathf.PerlinNoise(t, 1.3f) - .5f) * 2 * amount + cam.up * (Mathf.PerlinNoise(2.7f, t) - .5f) * 2 * amount;
            // It lunges in from below and settles over the view.
            Vector3 rise = -cam.up * viewHeight * .6f * (1 - ease);
            picture.transform.SetPositionAndRotation(cam.position + cam.forward * d + jerk + rise,
                cam.rotation * Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, 7.1f) - .5f) * (8 + 14 * gripJolt)));
            var parent = picture.transform.parent ? picture.transform.parent.lossyScale : Vector3.one;
            float s = size * Mathf.Lerp(.75f, 1, ease) * (1 + .04f * gripJolt);
            picture.transform.localScale = new Vector3(s / parent.x, s / parent.y, s / parent.z);
        }

        [Header("Grab")]
        public GrabSettings grab = new GrabSettings();

        bool holding;
        float holdUntil, nextSqueeze, gripShown, gripJolt;
        MaterialPropertyBlock gripBlock;
        int healthAtGrab;

        /// <summary>It holds the player in its grip.</summary>
        public bool Holding => holding;

        /// <summary>The strike landed on the player: it grabs.</summary>
        void StartHold()
        {
            holding = true;
            holdUntil = Time.time + grab.holdSeconds;
            nextSqueeze = Time.time + grab.squeezeInterval;
            healthAtGrab = Health;
        }

        /// <summary>
        /// While it holds: stands, faces the player, keeps him from walking and squeezes him now and then. False once the
        /// hold is over (time is up, it was hurt, the player got away); then it recovers as after any strike.
        /// </summary>
        bool ThinkHold()
        {
            if (!holding) return false;
            var target = Target;
            bool broken = Health < healthAtGrab || !Combat.IsAlive(target) || TargetDistance > contactRange + grab.slip;
            // The last squeeze falls on the hold's last moment: it still lands before the grip opens.
            if (!broken && Time.time >= nextSqueeze && nextSqueeze <= holdUntil + .001f) Squeeze(target);
            if (broken || Time.time >= holdUntil)
            {
                holding = false;
                if (State == CreatureState.Recover) stateTime = 0;
                return false;
            }
            Halt();
            Face(ToTarget);
            // Refreshed every frame: the grip lets go by itself a moment after the creature stops holding, whatever stops it.
            if (ReferenceEquals(target, world.Player)) world.motor.ApplyModifier(MotorModifier.MoveSpeed, 0, .2f);
            return true;
        }

        /// <summary>One squeeze of the grip: it lands even inside the player's protection after the previous one.</summary>
        void Squeeze(ICombatant target)
        {
            nextSqueeze += grab.squeezeInterval;
            squash.Kick(-2.5f);
            gripJolt = 1;
            world.Play(strikeCue, body.position, .7f);
            if (grab.squeezeHalfHearts <= 0) return;
            bool landed = target.TakeDamage(new DamageInfo(ScaledDamage(grab.squeezeHalfHearts, DamageScale), Vector3.zero,
                target.Position + Vector3.up * .9f, body.position, DamageKind.Hit, this, true));
            // A leech drinks what it took: its health comes back (its grip holds on; the hurt check counts from here).
            if (landed && grab.drainHeal > 0 && Health < maximumHealth)
            {
                Health = Mathf.Min(maximumHealth, Health + grab.drainHeal);
                healthAtGrab = Health;
            }
        }
    }
}
