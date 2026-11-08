using System;
using UnityEngine;

namespace Hakaton.Character
{
    [DefaultExecutionOrder(-100)]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        [Header("Authored components")]
        public CharacterController characterController;
        public FirstPersonInput input;
        public PlayerControlGate control;
        public PlayerStamina stamina;
        public FirstPersonView view;
        [Header("Ground movement")]
        [Min(.1f)] public float walkSpeed = 4;
        [Min(.1f)] public float sprintSpeed = 6.5f;
        [Min(.1f)] public float acceleration = 18;
        [Min(.1f)] public float braking = 24;
        [Range(0, 1)] public float airControl = .32f;
        [Header("Jump")]
        [Min(.1f)] public float jumpHeight = 1.2f;
        [Min(.1f)] public float gravity = 25;
        [Min(0)] public float coyoteTime = .12f;
        [Min(0)] public float jumpBufferTime = .14f;
        [Min(1)] public float terminalSpeed = 40;

        public Vector3 PlanarVelocity { get; private set; }
        public float Speed01 => Mathf.Clamp01(PlanarVelocity.magnitude / Mathf.Max(.1f, sprintSpeed));
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public float VerticalSpeed => verticalSpeed;
        public event Action<float> Landed;
        public event Action Jumped;
        float verticalSpeed, lastGrounded = float.NegativeInfinity, jumpBufferedUntil = float.NegativeInfinity;
        float lastJump = float.NegativeInfinity;
        bool hadAirTime;

        void Awake()
        {
            if (!characterController || !input || !control || !stamina || !view)
            { Debug.LogError("FirstPersonMotor needs its authored controller, input, control gate, stamina and view references.", this); enabled = false; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || !characterController.enabled) return;
            float now = Time.time;
            bool gameplay = control.GameplayActive && !control.CapturedThisFrame;
            bool grounded = characterController.isGrounded && now - lastJump > .08f;
            if (grounded)
            {
                lastGrounded = now;
                if (verticalSpeed < 0) verticalSpeed = -2;
            }
            else hadAirTime = true;

            if (gameplay && input.JumpPressed) jumpBufferedUntil = now + jumpBufferTime;
            if (!gameplay) jumpBufferedUntil = float.NegativeInfinity;
            Vector2 move = gameplay ? input.Move : Vector2.zero;
            bool wantsSprint = gameplay && input.SprintHeld && move.y > .1f && move.sqrMagnitude > .1f;
            IsSprinting = wantsSprint && stamina.CanSprint && grounded;
            float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 target = transform.TransformDirection(new Vector3(move.x, 0, move.y)) * targetSpeed;
            float rate = move.sqrMagnitude > .01f ? acceleration : braking;
            if (!grounded) rate *= airControl;
            if (!gameplay) { PlanarVelocity = Vector3.zero; target = Vector3.zero; }
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, target, rate * dt);

            if (gameplay && now <= jumpBufferedUntil && now - lastGrounded <= coyoteTime && stamina.TryJump())
            {
                verticalSpeed = Mathf.Sqrt(2 * gravity * jumpHeight);
                jumpBufferedUntil = lastGrounded = float.NegativeInfinity;
                lastJump = now; grounded = false; hadAirTime = true;
                IsSprinting = false;
                Jumped?.Invoke();
            }
            verticalSpeed = Mathf.Max(-terminalSpeed, verticalSpeed - gravity * dt);
            float impactSpeed = -verticalSpeed;
            var before = transform.position;
            CollisionFlags collisions = characterController.Move((PlanarVelocity + Vector3.up * verticalSpeed) * dt);
            if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            IsGrounded = (collisions & CollisionFlags.Below) != 0 && verticalSpeed <= 0;
            if (IsGrounded)
            {
                if (hadAirTime && impactSpeed > 2.5f) Landed?.Invoke(impactSpeed);
                hadAirTime = false;
                verticalSpeed = -2;
                lastGrounded = now;
            }
            Vector3 actual = (transform.position - before) / dt;
            PlanarVelocity = new Vector3(actual.x, 0, actual.z);
            // Running into a wall does not spend stamina or drive running feedback.
            IsSprinting = IsSprinting && PlanarVelocity.sqrMagnitude > .25f;
            stamina.Tick(dt, IsSprinting);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, rotation.eulerAngles.y, 0));
            characterController.enabled = wasEnabled;
            PlanarVelocity = Vector3.zero; verticalSpeed = 0;
            IsSprinting = IsGrounded = hadAirTime = false;
            lastGrounded = lastJump = jumpBufferedUntil = float.NegativeInfinity;
            input.Clear();
            view.ResetPose(rotation);
        }
    }
}
