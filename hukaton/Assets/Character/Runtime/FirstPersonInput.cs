using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Character
{
    /// <summary>Input sampling only; disabled capture never leaves cached movement behind.</summary>
    [DefaultExecutionOrder(-250)]
    public sealed class FirstPersonInput : MonoBehaviour
    {
        public PlayerControlGate control;
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool JumpPressed { get; private set; }

        void Update()
        {
            Clear();
            if (control == null || !control.GameplayActive || control.CapturedThisFrame) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                var direction = Vector2.zero;
                if (keyboard.wKey.isPressed) direction.y++;
                if (keyboard.sKey.isPressed) direction.y--;
                if (keyboard.dKey.isPressed) direction.x++;
                if (keyboard.aKey.isPressed) direction.x--;
                Move = Vector2.ClampMagnitude(direction, 1);
                SprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                JumpPressed = keyboard.spaceKey.wasPressedThisFrame;
            }
            if (Mouse.current != null) Look = Mouse.current.delta.ReadValue();
        }

        public void Clear()
        {
            Move = Look = Vector2.zero;
            SprintHeld = JumpPressed = false;
        }
        void OnDisable() => Clear();
    }
}
