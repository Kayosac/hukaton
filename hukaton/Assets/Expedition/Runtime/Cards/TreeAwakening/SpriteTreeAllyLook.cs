using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The awakened tree drawn as the owner's «Зловещее ожившее дерево» (atlas <c>Content/Enemies/SinisterLivingTree</c>):
    /// one billboarded hand-drawn drawing through <see cref="CreatureSpriteView"/>, like the sprite creatures (front or
    /// back by the camera, mirrored toward the heading). Walking and standing: the default drawing with a heavy waddle;
    /// the 0.8 s wind-up: the aggression drawing (the only moment its red «!» shows) crouching and trembling; the strike:
    /// the attack drawing's sweep with a lunge toward the target; a hit: the taking-damage drawing with a red flash;
    /// death: the death drawing (front or back) knocked down, then it sinks and fades. On awakening the drawing climbs out
    /// of the ground with a shudder; burrowing (to come up behind the player) is the same run backwards, down out of sight.
    /// Only presents the state: gameplay, colliders and sizes stay on <see cref="TreeAlly"/>.
    /// </summary>
    public sealed class SpriteTreeAllyLook : TreeAllyLook
    {
        [Tooltip("The atlas drawing (child): its height is the creature's height from the manifest (3 m).")]
        public CreatureSpriteView view;

        [Header("Walk")]
        [Min(0)] public float hopHeight = .03f;
        [Tooltip("Side-to-side roll of the waddle, degrees.")]
        [Range(0, 15)] public float waddle = 3;
        [Header("Awakening")]
        [Tooltip("How deep under the ground the drawing starts, m (the crown shows first).")]
        [Min(0)] public float awakenDepth = 2.3f;
        [Min(0)] public float awakenShake = .06f;
        [Header("Burrowing")]
        [Tooltip("How deep the drawing sinks when the tree burrows, m (all of its 3 m, so nothing sticks out).")]
        [Min(0)] public float burrowDepth = 3.3f;
        [Header("Attack")]
        [Tooltip("Wind-up crouch: share of the height the drawing squats by.")]
        [Range(0, .3f)] public float windupCrouch = .1f;
        [Min(0)] public float windupTremble = .035f;
        [Tooltip("Lean back in the wind-up and forward in the strike, degrees in the picture plane.")]
        [Range(0, 20)] public float windupLean = 4, strikeLean = 7;
        [Tooltip("The swing steps toward the target by this much, m: the sweep visually covers the 2.5 m reach.")]
        [Min(0)] public float strikeLunge = .5f;
        [Tooltip("The attack drawing stays this long after the strike window, s, so the swing reads.")]
        [Min(0)] public float attackHoldSeconds = .2f;
        [Header("Hit and death")]
        [Tooltip("The hand-drawn shader multiplies by this, so it only darkens toward red.")]
        public Color hurtTint = new Color(1f, .45f, .4f);
        [Min(0)] public float hurtJolt = .18f;
        [Min(0)] public float deathHop = .35f;
        [Tooltip("The fallen tree sinks this far while it fades, m.")]
        [Min(0)] public float sinkDepth = .3f;

        SpringFloat squash;
        float lastHurt, strikeEndedAt = float.NegativeInfinity, lunge, lastGait;
        TreeAllyPose lastPose = TreeAllyPose.Idle;
        bool wasAwakening;

        public override float Height => (view ? view.height : 3f) * transform.lossyScale.y;

        /// <summary>
        /// Which atlas drawing a moment of the tree reads as. The aggression drawing, with its red «!», shows only during
        /// the wind-up; the attack drawing covers the strike and <paramref name="attackHold"/> seconds after it; a fresh hit
        /// shows taking damage except while the branch is up or swinging (the telegraph and the swing stay readable).
        /// </summary>
        public static CreaturePose DrawingFor(TreeAllyPose pose, float hurt, float secondsSinceStrike, float attackHold)
        {
            switch (pose)
            {
                case TreeAllyPose.Dying: return CreaturePose.Death;
                case TreeAllyPose.Strike: return CreaturePose.Attack;
                case TreeAllyPose.Windup: return CreaturePose.Alarm;
                case TreeAllyPose.Recover when secondsSinceStrike < attackHold: return CreaturePose.Attack;
            }
            return hurt > 0 ? CreaturePose.Hurt : CreaturePose.Default;
        }

        void Awake() => squash.Snap(1);

        public override void Present(in TreeAllyLookState s, float deltaTime)
        {
            if (!view) return;
            float now = Time.time;
            if (lastPose == TreeAllyPose.Strike && s.Pose != TreeAllyPose.Strike) strikeEndedAt = now;
            bool awakening = s.Pose == TreeAllyPose.Awakening;
            if (wasAwakening && !awakening) squash.Kick(-5);
            wasAwakening = awakening;
            if (s.Pose == TreeAllyPose.Strike && lastPose != TreeAllyPose.Strike) squash.Kick(5);
            lastPose = s.Pose;

            // A fresh hit: a jolt back and a squash.
            if (s.Hurt > lastHurt + .5f) squash.Kick(-6);
            lastHurt = s.Hurt;

            float walking = Mathf.Clamp01(s.Speed / 1.2f);
            if (Mathf.FloorToInt(lastGait / Mathf.PI) != Mathf.FloorToInt(s.GaitPhase / Mathf.PI) && walking > .3f) squash.Kick(-1.2f);
            lastGait = s.GaitPhase;

            float sq = squash.Step(1, 7, .35f, deltaTime);
            var drawing = DrawingFor(s.Pose, s.Hurt, now - strikeEndedAt, attackHoldSeconds);
            float facing = view.spriteRenderer && view.spriteRenderer.flipX == view.facesRight ? -1 : 1;
            Vector3 offset = Vector3.zero;
            Vector2 scale = new Vector2(1 / Mathf.Sqrt(sq), sq);
            float roll = 0, alpha = 1;
            float lungeTarget = 0;

            switch (s.Pose)
            {
                case TreeAllyPose.Awakening:
                {
                    // Tears itself out of the ground: crown first, shuddering, roots last.
                    float p = s.Progress;
                    offset.y = -awakenDepth * (1 - Ease.OutBack(p, 1.2f));
                    offset += Shake(now, awakenShake * (1 - p) + .01f);
                    scale.y *= 1 + .08f * (1 - p);
                    roll = Mathf.Sin(now * 31) * 3 * (1 - p);
                    break;
                }
                case TreeAllyPose.Burrow:
                {
                    // Dives back under the ground (the awakening run backwards): a shudder and a little rise, then down
                    // roots first, the crown last; gone at the end.
                    float p = s.Progress;
                    float sink = p * p * (2.7f * p - 1.7f);
                    offset.y = -burrowDepth * sink;
                    offset += Shake(now, awakenShake * (1 - p * .5f) + .01f);
                    scale.y *= 1 + .08f * p;
                    roll = Mathf.Sin(now * 31) * 3 * (1 - p);
                    alpha = 1 - Mathf.Clamp01((p - .8f) / .2f);
                    break;
                }
                case TreeAllyPose.Windup:
                {
                    // Squats with the claws raised and trembles harder until the swing: the cue that a hit is coming.
                    float p = Ease.OutCubic(s.Progress);
                    scale.y *= 1 - windupCrouch * p;
                    scale.x *= 1 + windupCrouch * .5f * p;
                    offset += Shake(now, windupTremble * (.4f + s.Progress));
                    roll = windupLean * p * facing;
                    break;
                }
                case TreeAllyPose.Strike:
                {
                    float p = s.Progress;
                    lungeTarget = strikeLunge;
                    scale.x *= 1.12f;
                    scale.y *= .96f;
                    roll = -strikeLean * Ease.OutBack(Mathf.Clamp01(p * 2), 2) * facing;
                    break;
                }
                case TreeAllyPose.Dying:
                {
                    // Knocked over into the death drawing with a short hop, a landing squash, then it sinks and fades.
                    float p = s.Progress;
                    const float hopShare = .35f;
                    if (p < hopShare)
                    {
                        float k = p / hopShare;
                        offset.y = Mathf.Sin(k * Mathf.PI) * deathHop;
                        scale = new Vector2(.92f, 1.08f);
                    }
                    else
                    {
                        float settle = Ease.OutBack(Mathf.Clamp01((p - hopShare) / .3f));
                        scale = Vector2.LerpUnclamped(new Vector2(1.18f, .78f), Vector2.one, settle);
                    }
                    offset.y -= sinkDepth * Ease.InCubic(s.Fade);
                    alpha = 1 - s.Fade;
                    break;
                }
                default:
                {
                    // Stands swaying or walks with a heavy root waddle (the recovery after a swing walks too).
                    offset.y = Mathf.Abs(Mathf.Sin(s.GaitPhase)) * hopHeight * walking;
                    roll = Mathf.Sin(s.GaitPhase) * waddle * walking + Mathf.Sin(now * 1.3f) * 1.2f * (1 - walking);
                    if (s.Pose == TreeAllyPose.Recover && drawing == CreaturePose.Attack) lungeTarget = strikeLunge;
                    break;
                }
            }

            lunge = Mathf.Lerp(lunge, lungeTarget, Ease.Damp(lungeTarget > lunge ? 30 : 6, deltaTime));
            offset.z += lunge - hurtJolt * s.Hurt;
            view.Present(drawing, s.Heading, offset, scale, roll);
            Color tint = Color.Lerp(Color.white, hurtTint, s.Pose == TreeAllyPose.Dying ? 0 : Mathf.Clamp01(s.Hurt));
            tint.a = alpha;
            view.Tint(tint);
        }

        static Vector3 Shake(float time, float amount) =>
            new Vector3(Mathf.Sin(time * 41), Mathf.Sin(time * 29) * .5f, Mathf.Cos(time * 37)) * amount;
    }
}
