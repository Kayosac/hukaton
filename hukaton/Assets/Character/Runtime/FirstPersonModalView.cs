using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>Owns a fixed first-person view and changes it only behind an authored blackout.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class FirstPersonModalView : MonoBehaviour
    {
        [Header("Authored references")]
        public Camera viewCamera;
        public PlayerControlGate control;
        [Tooltip("A full-screen black graphic with a CanvasGroup, above the other UI.")]
        public CanvasGroup blinkOverlay;

        [Header("Blink")]
        [Min(0)] public float closeSeconds = .09f;
        [Min(0)] public float openSeconds = .11f;

        public bool Active { get; private set; }
        public bool IsTransitioning => phase != BlinkPhase.Idle;
        public Transform CurrentPose { get; private set; }

        enum BlinkPhase { Idle, Closing, Black, Opening }
        BlinkPhase phase;
        bool initialized, exitRequested, restoring;
        Transform requestedPose;
        float requestedFov, phaseTime, startingOpacity, restingFov, heldFov;
        Vector3 restingLocalPosition, heldPosition;
        Quaternion restingLocalRotation, heldRotation;

        void Awake()
        {
            if (!Initialize()) enabled = false;
        }

        bool Initialize()
        {
            if (initialized) return true;
            if (!viewCamera || !control || !blinkOverlay)
            {
                Debug.LogError("FirstPersonModalView needs its authored camera, control gate and full-screen blink overlay.", this);
                return false;
            }
            restingLocalPosition = viewCamera.transform.localPosition;
            restingLocalRotation = viewCamera.transform.localRotation;
            restingFov = viewCamera.fieldOfView;
            blinkOverlay.alpha = 0;
            blinkOverlay.blocksRaycasts = false;
            blinkOverlay.interactable = false;
            initialized = true;
            return true;
        }

        /// <summary>Claim control and blink to a saved camera pose. Repeating the same request is harmless.</summary>
        public bool Enter(Transform cameraPose, float fieldOfView) => SetPose(cameraPose, fieldOfView);

        /// <summary>The newest request wins if a previous blink is still closing.</summary>
        public bool SetPose(Transform cameraPose, float fieldOfView)
        {
            if (!isActiveAndEnabled || !cameraPose || !(fieldOfView > 0) || float.IsInfinity(fieldOfView)
                || !Initialize()) return false;
            if (cameraPose == viewCamera.transform || cameraPose.IsChildOf(viewCamera.transform))
            {
                Debug.LogError("A modal camera pose must be outside the camera's hierarchy.", this);
                return false;
            }
            fieldOfView = Mathf.Clamp(fieldOfView, 1, 179);
            if (Active && !exitRequested && requestedPose == cameraPose && Mathf.Approximately(requestedFov, fieldOfView))
                return true;

            if (!Active)
            {
                heldPosition = viewCamera.transform.position;
                heldRotation = viewCamera.transform.rotation;
                heldFov = viewCamera.fieldOfView;
                Active = true;
                restoring = false;
                control.SetBlocked(this, true);
            }
            exitRequested = false;
            requestedPose = cameraPose;
            requestedFov = fieldOfView;
            RequestBlink();
            return true;
        }

        /// <summary>Restore the FPS camera under black, then release only this component's control claim.</summary>
        public bool Exit()
        {
            if (!Active) return false;
            if (exitRequested) return true;
            exitRequested = true;
            requestedPose = null;
            RequestBlink();
            return true;
        }

        void RequestBlink()
        {
            blinkOverlay.blocksRaycasts = true;
            if (phase == BlinkPhase.Black)
            {
                ApplyRequestUnderBlack();
                return;
            }
            if (phase == BlinkPhase.Closing) return;
            startingOpacity = blinkOverlay.alpha;
            phaseTime = 0;
            phase = BlinkPhase.Closing;
        }

        void LateUpdate() => Advance(Time.unscaledDeltaTime);

        /// <summary>Advances the actual transition; also permits deterministic timing checks.</summary>
        public void Advance(float unscaledDeltaTime)
        {
            if (!Active || !initialized) return;
            if (!viewCamera || !control || !blinkOverlay) { Release(); return; }
            float dt = unscaledDeltaTime >= 0 && !float.IsInfinity(unscaledDeltaTime) ? unscaledDeltaTime : 0;

            if (phase == BlinkPhase.Closing)
            {
                phaseTime += dt;
                float duration = Mathf.Max(0, closeSeconds) * (1 - startingOpacity);
                float progress = duration > 0 ? Mathf.Clamp01(phaseTime / duration) : 1;
                blinkOverlay.alpha = Mathf.Lerp(startingOpacity, 1, Mathf.SmoothStep(0, 1, progress));
                if (progress >= 1)
                {
                    blinkOverlay.alpha = 1;
                    ApplyRequestUnderBlack();
                    phase = BlinkPhase.Black;
                    phaseTime = 0;
                    // Do not consume overflow: even a slow frame must render a fully black frame.
                }
            }
            else if (phase == BlinkPhase.Black || phase == BlinkPhase.Opening)
            {
                phase = BlinkPhase.Opening;
                phaseTime += dt;
                float duration = Mathf.Max(0, openSeconds);
                float progress = duration > 0 ? Mathf.Clamp01(phaseTime / duration) : 1;
                blinkOverlay.alpha = 1 - Mathf.SmoothStep(0, 1, progress);
                if (progress >= 1)
                {
                    phase = BlinkPhase.Idle;
                    blinkOverlay.alpha = 0;
                    blinkOverlay.blocksRaycasts = false;
                    if (exitRequested)
                    {
                        RestoreCamera();
                        Active = restoring = exitRequested = false;
                        control.SetBlocked(this, false);
                        return;
                    }
                }
            }
            PresentCamera();
        }

        void ApplyRequestUnderBlack()
        {
            if (exitRequested || !requestedPose)
            {
                exitRequested = restoring = true;
                CurrentPose = null;
                RestoreCamera();
                return;
            }
            restoring = false;
            CurrentPose = requestedPose;
            heldPosition = requestedPose.position;
            heldRotation = requestedPose.rotation;
            heldFov = requestedFov;
            PresentCamera();
        }

        void PresentCamera()
        {
            if (restoring) { RestoreCamera(); return; }
            viewCamera.transform.SetPositionAndRotation(heldPosition, heldRotation);
            viewCamera.fieldOfView = heldFov;
        }

        void RestoreCamera()
        {
            if (!viewCamera) return;
            viewCamera.transform.localPosition = restingLocalPosition;
            viewCamera.transform.localRotation = restingLocalRotation;
            viewCamera.fieldOfView = restingFov;
        }

        void Release()
        {
            if (initialized && Active) RestoreCamera();
            if (control) control.SetBlocked(this, false);
            if (blinkOverlay)
            {
                blinkOverlay.alpha = 0;
                blinkOverlay.blocksRaycasts = false;
            }
            Active = restoring = exitRequested = false;
            phase = BlinkPhase.Idle;
            CurrentPose = requestedPose = null;
        }

        void OnDisable() => Release();
    }
}
