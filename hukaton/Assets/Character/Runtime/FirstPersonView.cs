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
        [Header("Look")]
        [Min(.01f)] public float mouseSensitivity = .12f;
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
        float yaw, pitch, yawTarget, pitchTarget, yawVelocity, pitchVelocity;
        float baseFov, phase, landing, landingVelocity;
        Vector3 cameraBasePosition, cameraOffset;
        Quaternion cameraBaseRotation;

        void Awake()
        {
            if (!input || !control || !motor || !yawRoot || !pitchPivot || !cameraTransform || !viewCamera)
            { Debug.LogError("FirstPersonView needs its authored input, gate, motor, pivot and camera references.", this); enabled = false; return; }
            cameraBasePosition = cameraTransform.localPosition;
            cameraBaseRotation = cameraTransform.localRotation;
            baseFov = viewCamera.fieldOfView;
            yaw = yawTarget = yawRoot.eulerAngles.y;
            pitch = pitchTarget = SignedAngle(pitchPivot.localEulerAngles.x);
            motor.Landed += OnLanded;
        }

        void Update()
        {
            if (control.GameplayActive && !control.CapturedThisFrame)
            {
                yawTarget += input.Look.x * mouseSensitivity;
                pitchTarget = Mathf.Clamp(pitchTarget - input.Look.y * mouseSensitivity, -pitchLimit, pitchLimit);
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
            bool walking = control.GameplayActive && motor.IsGrounded && motor.PlanarVelocity.sqrMagnitude > .09f;
            float speedFactor = Mathf.Clamp01(motor.PlanarVelocity.magnitude / Mathf.Max(.1f, motor.walkSpeed));
            if (walking) phase += dt * (motor.IsSprinting ? sprintBobFrequency : walkBobFrequency)
                * Mathf.Lerp(.35f, 1, speedFactor) * Mathf.PI * 2;
            float gaitWeight = walking ? speedFactor * motionIntensity * (motor.IsSprinting ? sprintBobMultiplier : 1) : 0;
            float sway = Mathf.Sin(phase * .5f);
            Vector3 target = new Vector3(sway * bobAmplitude * .6f,
                Mathf.Sin(phase) * bobAmplitude, 0) * gaitWeight;
            cameraOffset = Vector3.Lerp(cameraOffset, target, 1 - Mathf.Exp(-motionResponse * dt));
            landing = Mathf.SmoothDamp(landing, 0, ref landingVelocity, .16f, Mathf.Infinity, dt);
            cameraTransform.localPosition = cameraBasePosition + cameraOffset + Vector3.down * landing * motionIntensity;
            float localLateral = yawRoot.InverseTransformDirection(motor.PlanarVelocity).x / Mathf.Max(.1f, motor.walkSpeed);
            float targetRoll = (control.GameplayActive ? -localLateral * strafeRoll * motionIntensity : 0)
                - sway * bobRoll * gaitWeight;
            cameraTransform.localRotation = Quaternion.Slerp(cameraTransform.localRotation,
                cameraBaseRotation * Quaternion.Euler(0, 0, targetRoll), 1 - Mathf.Exp(-motionResponse * dt));
            float fov = baseFov + (motor.IsSprinting ? sprintFov : 0);
            viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, fov, 1 - Mathf.Exp(-8 * dt));
        }

        public void ResetPose(Quaternion rotation)
        {
            yaw = yawTarget = rotation.eulerAngles.y;
            pitch = pitchTarget = Mathf.Clamp(SignedAngle(rotation.eulerAngles.x), -pitchLimit, pitchLimit);
            yawVelocity = pitchVelocity = landing = landingVelocity = phase = 0;
            cameraOffset = Vector3.zero;
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
