using Hakaton.Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The one press that reads on through spoken lines everywhere someone talks to the player (the tutorials, the
    /// goblin's word after a drowning, the mountain god): E or a left click. Nothing counts while the game is paused,
    /// nor on the frame the pause closes, so the key or click that closed a menu does not also close the line.
    /// </summary>
    public static class DialogueAdvance
    {
        static int suspendedFrame = -10;

        /// <summary>Pressed this frame: E or the left mouse button.</summary>
        public static bool Pressed
        {
            get
            {
                if (GamePause.Suspended) { suspendedFrame = Time.frameCount; return false; }
                if (Time.frameCount - suspendedFrame <= 1) return false;
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;
                return keyboard != null && keyboard.eKey.wasPressedThisFrame || mouse != null && mouse.leftButton.wasPressedThisFrame;
            }
        }
    }
}
