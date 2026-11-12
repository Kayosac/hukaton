using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.ShopBlockout
{
    public enum GoblinLiftState
    {
        Stowed,
        OpeningHatch,
        Lowering,
        Deployed,
        Raising,
        ClosingHatch
    }

    /// <summary>Animates an authored ceiling rig. The body, hatch, cables and poses are all saved assets.</summary>
    [DefaultExecutionOrder(600)]
    public sealed class SuspendedGoblin : MonoBehaviour
    {
        [Header("Authored mechanism")]
        public Transform goblinRoot, stowedPose, deployedPose, hatchHinge, pullHandle;
        [Tooltip("Common Y-only billboard for the goblin, rope sprites and ceiling attachment points.")]
        public Transform suspensionRoot;
        [Tooltip("Player camera, assigned by the scene.")]
        public Transform viewer;
        public SpriteRenderer[] cableSprites = new SpriteRenderer[2];
        public Transform[] cableTops = new Transform[2];
        public Transform[] cableHarnesses = new Transform[2];
        [Tooltip("The goblin's hand-drawn sprite: startles on the way down, pleased after a purchase.")]
        public SuspendedGoblinSprite drawing;
        public Vector3 closedHatchEuler = Vector3.zero;
        public Vector3 openHatchEuler = new Vector3(95, 0, 0);
        public bool startDeployed;
        [Tooltip("His card table: the overlay the player buys from once he is down.")]
        public Hakaton.Backroom.GoblinCardShop table;
        [Tooltip("The goblin's own collider (an upright capsule, the same from every side of the billboard). The aim always " +
                 "finds it; bodies are stopped only while he hangs still. On the way down or up — and after arriving, until " +
                 "the player has stepped out from under him — it lets them through, so he never lands on or shoves anyone.")]
        public Collider bodyCollider;

        [Header("Timing and weight")]
        [Min(.01f)]
        public float hatchSeconds = .36f;
        [Tooltip("Normalized hatch rotation: anticipation, a fast overshoot, then two settling bounces.")]
        public AnimationCurve hatchMotion = new AnimationCurve(
            new Keyframe(0, 0, 0, 0),
            new Keyframe(.12f, .025f, 0, 0),
            new Keyframe(.40f, 1.22f, 0, 0),
            new Keyframe(.64f, .86f, 0, 0),
            new Keyframe(.82f, 1.07f, 0, 0),
            new Keyframe(1, 1, 0, 0));
        [Tooltip("Squash/stretch along the hatch depth, with an opposing width change. The hinge stays fixed.")]
        public AnimationCurve hatchStretch = new AnimationCurve(
            new Keyframe(0, 0, 0, 0),
            new Keyframe(.12f, -.22f, 0, 0),
            new Keyframe(.26f, .22f, 0, 0),
            new Keyframe(.40f, -.22f, 0, 0),
            new Keyframe(.64f, .12f, 0, 0),
            new Keyframe(.82f, -.06f, 0, 0),
            new Keyframe(1, 0, 0, 0));
        [Min(.01f)]
        public float lowerSeconds = 1.15f;
        [Range(0, .1f)]
        public float landingOvershoot = .035f;
        [Range(0, .2f)]
        public float handlePullDistance = .08f;
        public AudioSource sound;
        public AudioClip pullSound, arriveSound;

        public GoblinLiftState State { get; private set; }
        public bool IsDeployed => State == GoblinLiftState.Deployed;
        public bool IsMoving => State != GoblinLiftState.Stowed && State != GoblinLiftState.Deployed;
        public bool IsStowed => State == GoblinLiftState.Stowed;

        bool initialized;
        float phaseTime;
        Vector3 handleRestPosition;
        Vector3 hatchRestScale;

        void Awake()
        {
            if (!Initialize()) enabled = false;
        }

        bool Initialize()
        {
            if (initialized) return true;
            if (!goblinRoot || !stowedPose || !deployedPose || !hatchHinge || !pullHandle || stowedPose.IsChildOf(goblinRoot)
                || deployedPose.IsChildOf(goblinRoot) || !suspensionRoot || !Complete(cableSprites) || !Complete(cableTops)
                || !Complete(cableHarnesses))
            {
                Debug.LogError(
                    "SuspendedGoblin needs an authored suspension, body, two external poses, hatch, handle and two complete sprite ropes.", this);
                return false;
            }
            initialized = true;
            handleRestPosition = pullHandle.localPosition;
            hatchRestScale = hatchHinge.localScale;
            State = startDeployed ? GoblinLiftState.Deployed : GoblinLiftState.Stowed;
            Present();
            return true;
        }

        static bool Complete<T>(T[] values) where T : Object => values != null && values.Length == 2 && values[0] && values[1];

        /// <summary>A repeated pull during the descent cannot restart or duplicate it.</summary>
        public bool Summon()
        {
            if (!Initialize() || State != GoblinLiftState.Stowed) return false;
            State = GoblinLiftState.OpeningHatch;
            phaseTime = 0;
            if (sound && pullSound) sound.PlayOneShot(pullSound, .4f);
            Present();
            return true;
        }

        /// <summary>Back up through the hatch once the player is done with him.</summary>
        public bool Raise()
        {
            if (!Initialize() || State != GoblinLiftState.Deployed) return false;
            State = GoblinLiftState.Raising;
            phaseTime = 0;
            if (sound && pullSound) sound.PlayOneShot(pullSound, .3f);
            Present();
            return true;
        }

        void Update() { Advance(Time.deltaTime); }

        void LateUpdate()
        {
            if (!initialized) return;
            if (viewer)
            {
                Vector3 away = suspensionRoot.position - viewer.position;
                away.y = 0;
                if (away.sqrMagnitude > .0001f)
                    suspensionRoot.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            UpdateCables();
        }

        public void Advance(float deltaTime)
        {
            if (!initialized || !(deltaTime > 0) || float.IsInfinity(deltaTime)) return;
            float remaining = deltaTime;
            while (IsMoving && remaining > 0)
            {
                bool hatchPhase = State == GoblinLiftState.OpeningHatch || State == GoblinLiftState.ClosingHatch;
                float duration = hatchPhase ? Mathf.Max(.01f, hatchSeconds) : Mathf.Max(.01f, lowerSeconds);
                float spent = Mathf.Min(remaining, Mathf.Max(0, duration - phaseTime));
                phaseTime += spent;
                remaining -= spent;
                if (phaseTime < duration) break;
                phaseTime = 0;
                switch (State)
                {
                    case GoblinLiftState.OpeningHatch: State = GoblinLiftState.Lowering; break;
                    case GoblinLiftState.Lowering:
                        State = GoblinLiftState.Deployed;
                        if (sound && arriveSound) sound.PlayOneShot(arriveSound, .25f);
                        break;
                    case GoblinLiftState.Raising: State = GoblinLiftState.ClosingHatch; break;
                    default:
                        State = GoblinLiftState.Stowed;
                        if (sound && arriveSound) sound.PlayOneShot(arriveSound, .18f);
                        break;
                }
            }
            Present();
        }

        void Present()
        {
            float hatchTime = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, hatchSeconds));
            float hatch = State == GoblinLiftState.Stowed ? 0
                : State == GoblinLiftState.OpeningHatch   ? hatchMotion.Evaluate(hatchTime)
                : State == GoblinLiftState.ClosingHatch   ? 1 - Ease.OutBack(hatchTime)
                                                          : 1;
            hatchHinge.localRotation =
                Quaternion.SlerpUnclamped(Quaternion.Euler(closedHatchEuler), Quaternion.Euler(openHatchEuler), hatch);
            float stretch = State == GoblinLiftState.OpeningHatch ? hatchStretch.Evaluate(hatchTime) : 0;
            hatchHinge.localScale = Vector3.Scale(hatchRestScale, new Vector3(1 - stretch * .65f, 1, 1 + stretch));
            float lowerTime = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, lowerSeconds));
            float lower = State == GoblinLiftState.Deployed ? 1
                : State == GoblinLiftState.Lowering         ? lowerTime
                : State == GoblinLiftState.Raising          ? 1 - lowerTime
                                                            : 0;
            // Down with a bounce; up he is yanked: slow start, quick finish.
            float eased = State == GoblinLiftState.Raising ? 1 - Ease.InCubic(lowerTime) : 1 - Mathf.Pow(1 - lower, 3);
            Vector3 position = Vector3.Lerp(stowedPose.position, deployedPose.position, eased);
            if (State != GoblinLiftState.Raising) position += Vector3.down * (Mathf.Sin(lower * Mathf.PI) * lower * lower * landingOvershoot);
            goblinRoot.position = position;
            float pull = State == GoblinLiftState.OpeningHatch
                ? Mathf.Sin(Mathf.Clamp01(phaseTime / Mathf.Max(.01f, hatchSeconds)) * Mathf.PI)
                : 0;
            pullHandle.localPosition = handleRestPosition + Vector3.down * (pull * handlePullDistance);
            UpdateCables();
            UpdateBodyCollider();
        }

        static readonly Collider[] Overlapping = new Collider[8];

        void UpdateBodyCollider()
        {
            if (!bodyCollider) return;
            // Once solid he stays solid until he moves again; he turns solid only when nobody stands inside him.
            bool solid = State == GoblinLiftState.Deployed && (!bodyCollider.isTrigger || !OverlapsWalkingBody());
            if (bodyCollider.isTrigger == solid) bodyCollider.isTrigger = !solid;
        }

        /// <summary>A walking character (CharacterController) is inside the space the goblin occupies.</summary>
        bool OverlapsWalkingBody()
        {
            if (!bodyCollider.enabled || !bodyCollider.gameObject.activeInHierarchy) return false;
            int count;
            if (bodyCollider is CapsuleCollider capsule && capsule.direction == 1)
            {
                Transform body = capsule.transform;
                Vector3 scale = body.lossyScale;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float half = Mathf.Max(0, capsule.height * .5f * Mathf.Abs(scale.y) - radius);
                Vector3 center = body.TransformPoint(capsule.center), axis = body.up * half;
                count = Physics.OverlapCapsuleNonAlloc(center - axis, center + axis, radius, Overlapping, Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
            }
            else
            {
                Bounds bounds = bodyCollider.bounds;
                count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, Overlapping, Quaternion.identity, Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
            }
            for (int i = 0; i < count; i++)
                if (Overlapping[i] is CharacterController) return true;
            return false;
        }

        void UpdateCables()
        {
            for (int i = 0; i < 2; i++) UpdateCable(cableSprites[i], cableTops[i].position, cableHarnesses[i].position);
        }

        static void UpdateCable(SpriteRenderer rope, Vector3 top, Vector3 bottom)
        {
            Transform cable = rope.transform;
            // All attachment points share the billboard plane; only the rope's visible length changes.
            if (cable.parent)
            {
                top = cable.parent.InverseTransformPoint(top);
                bottom = cable.parent.InverseTransformPoint(bottom);
            }
            Vector3 direction = top - bottom;
            cable.localPosition = (top + bottom) * .5f;
            if (direction.sqrMagnitude > .000001f)
                cable.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg);
            // Tiled SpriteRenderers keep the strand spacing and width constant throughout descent.
            rope.size = new Vector2(rope.size.x, Mathf.Max(.00001f, direction.magnitude));
        }
    }
}
