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
    public sealed class SuspendedGoblin : MonoBehaviour
    {
        [Header("Authored mechanism")]
        public Transform goblinRoot, stowedPose, deployedPose, hatchHinge, pullHandle;
        public Transform[] cableMeshes = new Transform[2];
        public Transform[] cableTops = new Transform[2];
        public Transform[] cableHarnesses = new Transform[2];
        [Tooltip("Length of each cable mesh at local Y scale 1. Unity cylinders are 2 units tall.")]
        [Min(.001f)]
        public float cableMeshLength = 2;
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
                || deployedPose.IsChildOf(goblinRoot) || !Complete(cableMeshes) || !Complete(cableTops)
                || !Complete(cableHarnesses))
            {
                Debug.LogError(
                    "SuspendedGoblin needs an authored body, two external poses, hatch, handle and two complete cables.", this);
                return false;
            }
            initialized = true;
            handleRestPosition = pullHandle.localPosition;
            State = startDeployed ? GoblinLiftState.Deployed : GoblinLiftState.Stowed;
            Present();
            return true;
        }

        static bool Complete(Transform[] values) => values != null && values.Length == 2 && values[0] && values[1];

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
            goblinRoot.SetPositionAndRotation(position, Quaternion.Slerp(stowedPose.rotation, deployedPose.rotation, eased));
            float pull = State == GoblinLiftState.OpeningHatch
                ? Mathf.Sin(Mathf.Clamp01(phaseTime / Mathf.Max(.01f, hatchSeconds)) * Mathf.PI)
                : 0;
            pullHandle.localPosition = handleRestPosition + Vector3.down * (pull * handlePullDistance);
            for (int i = 0; i < 2; i++) UpdateCable(cableMeshes[i], cableTops[i].position, cableHarnesses[i].position);
        }

        void UpdateCable(Transform cable, Vector3 top, Vector3 bottom)
        {
            // Work in the mesh parent's coordinates so authored rig rotation/scale remains editable.
            if (cable.parent)
            {
                top = cable.parent.InverseTransformPoint(top);
                bottom = cable.parent.InverseTransformPoint(bottom);
            }
            Vector3 direction = bottom - top;
            cable.localPosition = (top + bottom) * .5f;
            if (direction.sqrMagnitude > .000001f) cable.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            Vector3 scale = cable.localScale;
            scale.y = Mathf.Max(.00001f, direction.magnitude / Mathf.Max(.001f, cableMeshLength));
            cable.localScale = scale;
        }
    }
}
