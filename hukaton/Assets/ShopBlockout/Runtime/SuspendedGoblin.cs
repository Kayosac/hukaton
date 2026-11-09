using UnityEngine;

namespace Hakaton.ShopBlockout
{
    public enum GoblinLiftState
    {
        Stowed,
        OpeningHatch,
        Lowering,
        Deployed
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
        public Vector3 openHatchEuler = new Vector3(0, 0, -105);
        public bool startDeployed;

        [Header("Timing and weight")]
        [Min(.01f)]
        public float hatchSeconds = .28f;
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
        public bool IsMoving => State == GoblinLiftState.OpeningHatch || State == GoblinLiftState.Lowering;

        bool initialized;
        float phaseTime;
        Vector3 handleRestPosition;

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
                float duration =
                    State == GoblinLiftState.OpeningHatch ? Mathf.Max(.01f, hatchSeconds) : Mathf.Max(.01f, lowerSeconds);
                float spent = Mathf.Min(remaining, Mathf.Max(0, duration - phaseTime));
                phaseTime += spent;
                remaining -= spent;
                if (phaseTime < duration) break;
                phaseTime = 0;
                if (State == GoblinLiftState.OpeningHatch)
                    State = GoblinLiftState.Lowering;
                else
                {
                    State = GoblinLiftState.Deployed;
                    if (sound && arriveSound) sound.PlayOneShot(arriveSound, .25f);
                }
            }
            Present();
        }

        void Present()
        {
            float hatch = State == GoblinLiftState.Stowed ? 0
                : State == GoblinLiftState.OpeningHatch   ? Mathf.SmoothStep(0, 1, phaseTime / Mathf.Max(.01f, hatchSeconds))
                                                          : 1;
            hatchHinge.localRotation =
                Quaternion.Slerp(Quaternion.Euler(closedHatchEuler), Quaternion.Euler(openHatchEuler), hatch);
            float lower = State == GoblinLiftState.Deployed ? 1
                : State == GoblinLiftState.Lowering         ? Mathf.Clamp01(phaseTime / Mathf.Max(.01f, lowerSeconds))
                                                            : 0;
            float eased = 1 - Mathf.Pow(1 - lower, 3);
            Vector3 position = Vector3.Lerp(stowedPose.position, deployedPose.position, eased);
            position += Vector3.down * (Mathf.Sin(lower * Mathf.PI) * lower * lower * landingOvershoot);
            goblinRoot.position = position;
            float pull = State == GoblinLiftState.OpeningHatch
                ? Mathf.Sin(Mathf.Clamp01(phaseTime / Mathf.Max(.01f, hatchSeconds)) * Mathf.PI)
                : 0;
            pullHandle.localPosition = handleRestPosition + Vector3.down * (pull * handlePullDistance);
            UpdateCables();
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
