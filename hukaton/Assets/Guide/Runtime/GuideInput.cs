using Hakaton.Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Guide
{
    /// <summary>
    /// The guides' keys: E or a left click reads on (the game's rule for every spoken line), Esc skips the tour.
    /// Nothing counts while the game is paused, nor on the frame the pause closes.
    /// </summary>
    public static class GuideInput
    {
        static int suspendedFrame = -10;

        static bool Free
        {
            get
            {
                if (GamePause.Suspended) { suspendedFrame = Time.frameCount; return false; }
                return Time.frameCount - suspendedFrame > 1;
            }
        }

        public static bool Advance
        {
            get
            {
                if (!Free) return false;
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;
                return keyboard != null && keyboard.eKey.wasPressedThisFrame || mouse != null && mouse.leftButton.wasPressedThisFrame;
            }
        }

        public static bool Skip
        {
            get
            {
                if (!Free || GamePause.EscapeConsumed) return false;
                var keyboard = Keyboard.current;
                if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return false;
                GamePause.ConsumeEscape();
                return true;
            }
        }
    }
}
