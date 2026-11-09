using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// TEMPORARY look of the awakened tree (owner's decision: the art comes later): a crooked walking trunk on two
    /// root legs with one branch arm, animated procedurally from authored pivots. Readable states: rising out of the
    /// ground, waddling walk, the branch raised high and trembling for the wind-up, an overhead chop, a red flash and
    /// jolt when hit, toppling over and sinking when it dies. Replace the whole child prefab to change the look.
    /// </summary>
    public sealed class PlaceholderTreeLook : TreeAllyLook
    {
        [Header("Authored pivots")]
        [Tooltip("Everything above the legs; leans, sways and topples.")]
        public Transform body;
        [Tooltip("Shoulder of the branch arm; the branch hangs along its local -Y and swings about local X.")]
        public Transform arm;
        public Transform legLeft, legRight;
        public Transform crown;
        [Tooltip("Renderers flashed red on a hit.")]
        public Renderer[] tinted;
        [Header("Shape")]
        [Min(.1f)] public float height = 2.8f;
        [Header("Arm angles about local X (negative = forward)")]
        public float armRest = -28, armRaised = -200, armChop = -55;
        [Header("Motion")]
        [Range(0, 60)] public float legSwing = 28;
        [Range(0, 20)] public float waddle = 6;
        [Min(0)] public float bob = .07f;
        [Range(0, 30)] public float windupLean = 12, strikeLean = 16;
        [Min(0)] public float awakenDepth = 1.8f;
        public Color hurtColour = new Color(1f, .35f, .3f);

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock block;
        Color[] baseColours;
        Quaternion bodyRest, armRestRotation, crownRest, legLeftRest, legRightRest;
        Vector3 bodyRestPosition, rootScale;
        SpringFloat jolt;
        float lastHurt;

        public override float Height => height * transform.lossyScale.y;

        void Awake()
        {
            if (body) { bodyRest = body.localRotation; bodyRestPosition = body.localPosition; }
            if (arm) armRestRotation = arm.localRotation;
            if (crown) crownRest = crown.localRotation;
            if (legLeft) legLeftRest = legLeft.localRotation;
            if (legRight) legRightRest = legRight.localRotation;
            rootScale = transform.localScale;
            block = new MaterialPropertyBlock();
            baseColours = new Color[tinted != null ? tinted.Length : 0];
            for (int i = 0; i < baseColours.Length; i++)
            {
                var material = tinted[i] ? tinted[i].sharedMaterial : null;
                baseColours[i] = material && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
            }
        }

        public override void Present(in TreeAllyLookState s, float deltaTime)
        {
            if (!body) return;
            float t = Time.time;
            if (s.Hurt > lastHurt + .5f) jolt.Kick(-9);
            lastHurt = s.Hurt;
            float joltValue = jolt.Step(0, 5, .3f, deltaTime);

            float lean = 0, roll = 0, lift = 0, armAngle = armRest, legs = 0, shake = 0;
            float walking = Mathf.Clamp01(s.Speed / 1.5f);
            switch (s.Pose)
            {
                case TreeAllyPose.Awakening:
                {
                    // Tears itself out of the ground: rises with a shudder, roots kicking.
                    float p = s.Progress;
                    lift = -awakenDepth * (1 - Ease.OutBack(p, 1.2f));
                    shake = (1 - p) * 4;
                    legs = Mathf.Sin(t * 30) * (1 - p);
                    armAngle = Mathf.Lerp(-170, armRest, Ease.OutCubic(p));
                    break;
                }
                case TreeAllyPose.Windup:
                {
                    // The branch goes up and back over the crown and trembles there: the cue to step away.
                    float p = s.Progress;
                    armAngle = Mathf.Lerp(armRest, armRaised, Ease.OutCubic(Mathf.Clamp01(p * 1.6f)));
                    lean = -windupLean * Ease.OutCubic(p);
                    shake = 1.5f + p * 2.5f;
                    break;
                }
                case TreeAllyPose.Strike:
                {
                    float p = s.Progress;
                    armAngle = Mathf.Lerp(armRaised, armChop, Ease.OutCubic(Mathf.Clamp01(p * 2.5f)));
                    lean = strikeLean * Ease.OutBack(Mathf.Clamp01(p * 2), 2);
                    break;
                }
                case TreeAllyPose.Recover:
                {
                    float p = s.Progress;
                    armAngle = Mathf.Lerp(armChop, armRest, Ease.InOutSine(p));
                    lean = strikeLean * (1 - Ease.InOutSine(Mathf.Clamp01(p * 1.5f)));
                    legs = Mathf.Sin(s.GaitPhase) * walking;
                    roll = Mathf.Sin(s.GaitPhase) * waddle * walking;
                    break;
                }
                case TreeAllyPose.Dying:
                {
                    // Topples backward with a bounce, the branch flops, then it sinks into the ground.
                    float p = s.Progress;
                    lean = -88 * Ease.OutBack(Mathf.Clamp01(p * 2.2f), 1.1f);
                    roll = 14 * Ease.OutCubic(p);
                    armAngle = Mathf.Lerp(armRest, 20, Ease.OutCubic(p));
                    lift = -.35f * Ease.InCubic(p);
                    break;
                }
                default:
                {
                    // Idle sway or a heavy waddle with root legs swinging.
                    legs = Mathf.Sin(s.GaitPhase) * walking;
                    roll = Mathf.Sin(s.GaitPhase) * waddle * walking + Mathf.Sin(t * 1.3f) * 2 * (1 - walking);
                    lift = Mathf.Abs(Mathf.Sin(s.GaitPhase)) * bob * walking;
                    lean = 4 * walking;
                    armAngle = armRest + Mathf.Sin(s.GaitPhase) * 14 * walking + Mathf.Sin(t * 1.1f) * 3;
                    break;
                }
            }
            lean += joltValue;
            Vector3 tremble = shake > 0 ? new Vector3(Mathf.Sin(t * 41), 0, Mathf.Cos(t * 37)) * .012f * shake : Vector3.zero;
            body.localPosition = bodyRestPosition + Vector3.up * lift + tremble;
            body.localRotation = bodyRest * Quaternion.Euler(lean, 0, roll);
            if (arm) arm.localRotation = armRestRotation * Quaternion.Euler(armAngle - armRest, 0, 0);
            if (crown) crown.localRotation = crownRest * Quaternion.Euler(Mathf.Sin(t * 23) * shake, 0, Mathf.Cos(t * 19) * shake);
            if (legLeft) legLeft.localRotation = legLeftRest * Quaternion.Euler(legs * legSwing, 0, 0);
            if (legRight) legRight.localRotation = legRightRest * Quaternion.Euler(-legs * legSwing, 0, 0);
            transform.localScale = rootScale * Mathf.Lerp(1, .05f, Ease.InCubic(s.Fade));
            Tint(s.Hurt);
        }

        void Tint(float amount)
        {
            if (tinted == null || block == null) return;
            for (int i = 0; i < tinted.Length; i++)
            {
                var r = tinted[i];
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, Color.Lerp(baseColours[i], hurtColour, Mathf.Clamp01(amount)));
                r.SetPropertyBlock(block);
            }
        }
    }
}
