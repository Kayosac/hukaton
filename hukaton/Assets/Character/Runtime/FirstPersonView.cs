using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>Mouse look and restrained locomotion feedback on an authored camera hierarchy.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class FirstPersonView : MonoBehaviour
    {
        public FirstPersonInput input;
        public PlayerControlGate control;
        public FirstPersonMotor motor;
        public Transform yawRoot, pitchPivot, cameraTransform;
        public Camera viewCamera;
        [Tooltip("Optional additive shake, recoil and FOV punches.")]
        public CameraFeel feel;
        [Header("Look")]
        [Min(.01f)] public float mouseSensitivity = .12f;
        /// <summary>The player's setting (Settings → mouse sensitivity) on top of the authored value.</summary>
        [System.NonSerialized] public float sensitivityScale = 1;
        [Min(0)] public float lookSmoothTime = .025f;
        [Range(30, 89)] public float pitchLimit = 82;
        [Header("Camera motion — set intensity to zero to disable")]
        [Range(0, 1)] public float motionIntensity = 1;
        [Min(0)] public float bobAmplitude = .055f;
        [Min(1)] public float sprintBobMultiplier = 1.45f;
        [Range(0, 3)] public float bobRoll = .65f;
        [Min(.1f)] public float walkBobFrequency = 1.8f;
        [Min(.1f)] public float sprintBobFrequency = 2.5f;
        [Range(0, 3)] public float strafeRoll = .8f;
        [Range(0, 12)] public float sprintFov = 5;
        [Range(0, .1f)] public float maxLandingDip = .045f;
        [Min(.1f)] public float motionResponse = 14;
        [Header("Steps — the eye eases over a step instead of popping")]
        [Tooltip("Seconds the eye takes to catch up after the body stepped up or down. 0 shows every step at once.")]
        [Min(0)] public float stepSmoothTime = .08f;
        [Tooltip("Largest height change (m) in one frame treated as a step, and largest eye offset. Bigger changes (falls, " +
                 "teleports) are shown at once.")]
        [Min(0)] public float maxStepSmoothing = .45f;
        [Tooltip("The eye follows the slope under the feet exactly up to this angle (no lag on ramps); only what the body does " +
                 "beyond it, like dropping off a step's edge, is eased.")]
        [Range(0, 89)] public float stepSlopeAllowance = 35;
        // Slack (rise per metre walked) on top of the slope under the feet, for rough ground.
        const float StepSlopeTolerance = .05f;
        float eyeStep, eyeStepVelocity;
        Vector3 lastBody;
        bool lastBodyOnGround, hasLastBody;
        float yaw, pitch, yawTarget, pitchTarget, yawVelocity, pitchVelocity;
        float baseFov, smoothedFov, phase, landing, landingVelocity;
        Quaternion roll = Quaternion.identity;
        Vector3 cameraBasePosition, cameraOffset;
        Quaternion cameraBaseRotation;

        void Awake()
        {
            if (!input || !control || !motor || !yawRoot || !pitchPivot || !cameraTransform || !viewCamera)
            { Debug.LogError("FirstPersonView needs its authored input, gate, motor, pivot and camera references.", this); enabled = false; return; }
            cameraBasePosition = cameraTransform.localPosition;
            cameraBaseRotation = cameraTransform.localRotation;
            baseFov = smoothedFov = viewCamera.fieldOfView;
            yaw = yawTarget = yawRoot.eulerAngles.y;
            pitch = pitchTarget = SignedAngle(pitchPivot.localEulerAngles.x);
            motor.Landed += OnLanded;
        }

        Vector3? guide;

        /// <summary>Turns the view toward a point and holds it there (whoever is talking in a cutscene); null frees the mouse.</summary>
        public void Guide(Vector3? point) => guide = point;

        void Update()
        {
            if (guide.HasValue)
            {
                Vector3 to = guide.Value - pitchPivot.position;
                if (to.sqrMagnitude > .0001f)
                {
                    float wantedYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float wantedPitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1, 1)) * Mathf.Rad2Deg;
                    yawTarget = yaw + Mathf.DeltaAngle(yaw, wantedYaw);
                    pitchTarget = Mathf.Clamp(wantedPitch, -pitchLimit, pitchLimit);
                }
                float step = Time.deltaTime;
                if (step <= 0) return;
                yaw = Mathf.SmoothDampAngle(yaw, yawTarget, ref yawVelocity, .22f, Mathf.Infinity, step);
                pitch = Mathf.SmoothDampAngle(pitch, pitchTarget, ref pitchVelocity, .22f, Mathf.Infinity, step);
                yawRoot.rotation = Quaternion.Euler(0, yaw, 0);
                pitchPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
                return;
            }
            if (input.LookBlocked) return;
            if (control.GameplayActive && !control.CapturedThisFrame)
            {
                float sensitivity = mouseSensitivity * sensitivityScale;
                yawTarget += input.Look.x * sensitivity;
                pitchTarget = Mathf.Clamp(pitchTarget - input.Look.y * sensitivity, -pitchLimit, pitchLimit);
            }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            yaw = Mathf.SmoothDampAngle(yaw, yawTarget, ref yawVelocity, lookSmoothTime, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDampAngle(pitch, pitchTarget, ref pitchVelocity, lookSmoothTime, Mathf.Infinity, dt);
            yawRoot.rotation = Quaternion.Euler(0, yaw, 0);
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        void LateUpdate()
        {
            if (!viewCamera || !motor) return;
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            bool walking = control.GameplayActive && motor.GroundedForFeedback && motor.PlanarVelocity.sqrMagnitude > .09f;
            float speedFactor = Mathf.Clamp01(motor.PlanarVelocity.magnitude / Mathf.Max(.1f, motor.walkSpeed));
            if (walking) phase += dt * (motor.IsSprinting ? sprintBobFrequency : walkBobFrequency)
                * Mathf.Lerp(.35f, 1, speedFactor) * Mathf.PI * 2;
            float gaitWeight = walking ? speedFactor * motionIntensity * (motor.IsSprinting ? sprintBobMultiplier : 1) : 0;
            float sway = Mathf.Sin(phase * .5f);
            Vector3 target = new Vector3(sway * bobAmplitude * .6f,
                Mathf.Sin(phase) * bobAmplitude, 0) * gaitWeight;
            cameraOffset = Vector3.Lerp(cameraOffset, target, 1 - Mathf.Exp(-motionResponse * dt));
            landing = Mathf.SmoothDamp(landing, 0, ref landingVelocity, .16f, Mathf.Infinity, dt);
            FollowSteps(dt);
            Vector3 feelPosition = feel ? feel.PositionOffset : Vector3.zero;
            // The step offset is a world height: convert it into the (pitched) camera parent's space.
            var cameraParent = cameraTransform.parent;
            Vector3 stepPosition = cameraParent ? cameraParent.InverseTransformVector(Vector3.up * eyeStep) : Vector3.up * eyeStep;
            cameraTransform.localPosition = cameraBasePosition + cameraOffset + Vector3.down * landing * motionIntensity + feelPosition
                + stepPosition;
            float localLateral = yawRoot.InverseTransformDirection(motor.PlanarVelocity).x / Mathf.Max(.1f, motor.walkSpeed);
            float targetRoll = (control.GameplayActive ? -localLateral * strafeRoll * motionIntensity : 0)
                - sway * bobRoll * gaitWeight;
            roll = Quaternion.Slerp(roll, Quaternion.Euler(0, 0, targetRoll), 1 - Mathf.Exp(-motionResponse * dt));
            cameraTransform.localRotation = cameraBaseRotation * roll * (feel ? feel.RotationOffset : Quaternion.identity);
            float fov = baseFov + (motor.IsSprinting ? sprintFov : 0);
            smoothedFov = Mathf.Lerp(smoothedFov, fov, 1 - Mathf.Exp(-8 * dt));
            viewCamera.fieldOfView = smoothedFov + (feel ? feel.FovOffset : 0);
        }

        /// <summary>
        /// Steps lift or drop the body by a step's height within a frame or two. While it walks, the eye keeps its height
        /// through such a change and then eases to the body (eyeStep, world metres). Only what goes beyond the slope under
        /// the feet (<see cref="FirstPersonMotor.GroundNormal"/>, capped by <see cref="stepSlopeAllowance"/>) is eased, so
        /// ramps are followed exactly; jumps, falls and teleports are shown as they are.
        /// </summary>
        void FollowSteps(float dt)
        {
            Vector3 body = motor.transform.position;
            bool onGround = motor.GroundedForFeedback;
            if (hasLastBody && onGround && lastBodyOnGround && stepSmoothTime > 0)
            {
                Vector3 delta = body - lastBody;
                float run = new Vector2(delta.x, delta.z).magnitude;
                if (Mathf.Abs(delta.y) <= maxStepSmoothing && run < 1)
                {
                    float slope = Mathf.Min(stepSlopeAllowance, Vector3.Angle(motor.GroundNormal, Vector3.up));
                    float allowance = run * (Mathf.Tan(slope * Mathf.Deg2Rad) + StepSlopeTolerance);
                    float excess = delta.y - Mathf.Clamp(delta.y, -allowance, allowance);
                    eyeStep = Mathf.Clamp(eyeStep - excess, -maxStepSmoothing, maxStepSmoothing);
                }
            }
            if (stepSmoothTime > 0) eyeStep = Mathf.SmoothDamp(eyeStep, 0, ref eyeStepVelocity, stepSmoothTime, Mathf.Infinity, dt);
            else eyeStep = eyeStepVelocity = 0;
            lastBody = body;
            lastBodyOnGround = onGround;
            hasLastBody = true;
        }

        /// <summary>Drops any step easing (teleports): the eye is at its authored height at once.</summary>
        public void ClearStepSmoothing()
        {
            eyeStep = eyeStepVelocity = 0;
            hasLastBody = false;
        }

        public void ResetPose(Quaternion rotation)
        {
            yaw = yawTarget = rotation.eulerAngles.y;
            pitch = pitchTarget = Mathf.Clamp(SignedAngle(rotation.eulerAngles.x), -pitchLimit, pitchLimit);
            yawVelocity = pitchVelocity = landing = landingVelocity = phase = 0;
            cameraOffset = Vector3.zero;
            ClearStepSmoothing();
            roll = Quaternion.identity;
            smoothedFov = baseFov;
            if (feel) feel.ResetFeel();
            yawRoot.rotation = Quaternion.Euler(0, yaw, 0);
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            cameraTransform.localPosition = cameraBasePosition;
            cameraTransform.localRotation = cameraBaseRotation;
            viewCamera.fieldOfView = baseFov;
        }

        void OnLanded(float speed) { landing = Mathf.Clamp(speed * .005f, 0, maxLandingDip); landingVelocity = 0; }
        static float SignedAngle(float value) => value > 180 ? value - 360 : value;
        void OnDestroy() { if (motor) motor.Landed -= OnLanded; }
    }
}
