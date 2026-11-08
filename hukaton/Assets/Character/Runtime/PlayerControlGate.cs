using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Character
{
    /// <summary>Owns cursor capture and independent modal control claims.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class PlayerControlGate : MonoBehaviour
    {
        public bool captureOnStart = true;
        readonly HashSet<Object> blockers = new HashSet<Object>();
        bool focused, captured, restoreAfterModal;
        int captureFrame = -1;

        public bool GameplayActive => enabled && focused && captured && blockers.Count == 0
            && Cursor.lockState == CursorLockMode.Locked;
        public bool CapturedThisFrame => captureFrame == Time.frameCount;
        public bool HasModalOwner => blockers.Count > 0;

        void Start()
        {
            focused = Application.isFocused;
            if (captureOnStart && focused && blockers.Count == 0) CaptureCursor();
        }

        void Update()
        {
            // Destroyed scene owners must not permanently strand the player in a modal state.
            if (blockers.RemoveWhere(owner => !owner) > 0 && blockers.Count == 0) RestoreAfterModal();
            if (!focused || blockers.Count > 0) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { ReleaseCursor(); return; }
            if (captured && Cursor.lockState != CursorLockMode.Locked) captured = false;
            if (!captured && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                CaptureCursor();
        }

        /// <summary>Idempotent ownership: two open interfaces cannot accidentally release one another.</summary>
        public void SetBlocked(Object owner, bool blocked)
        {
            if (!owner) return;
            if (blocked)
            {
                if (blockers.Count == 0) restoreAfterModal = captured;
                if (blockers.Add(owner)) SetCapture(false);
            }
            else if (blockers.Remove(owner) && blockers.Count == 0) RestoreAfterModal();
        }

        public void CaptureCursor()
        {
            if (!focused || blockers.Count > 0 || !enabled) return;
            SetCapture(true);
            captureFrame = Time.frameCount;
        }

        public void ReleaseCursor()
        {
            restoreAfterModal = false;
            SetCapture(false);
        }

        void RestoreAfterModal()
        {
            bool restore = restoreAfterModal;
            restoreAfterModal = false;
            if (restore && focused) CaptureCursor();
        }

        void SetCapture(bool value)
        {
            captured = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (!hasFocus) ReleaseCursor();
        }

        void OnDisable()
        {
            blockers.Clear();
            ReleaseCursor();
        }
    }
}
