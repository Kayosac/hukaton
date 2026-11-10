using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hakaton.Character
{
    /// <summary>
    /// Shared state of the game menus. <see cref="Paused"/>: the pause menu has frozen the world. <see cref="Suspended"/>:
    /// a menu (main or pause) is in front, so gameplay that reads keys directly (dialogue lines on E, trading hotkeys,
    /// the shift clock) waits. Escape belongs to one listener per frame: the menu that used it calls
    /// <see cref="ConsumeEscape"/>, everyone else checks <see cref="EscapeConsumed"/>.
    /// </summary>
    public static class GamePause
    {
        static readonly HashSet<Object> suspenders = new HashSet<Object>();
        static int escapeConsumedFrame = -10;

        public static bool Paused { get; private set; }
        public static bool Suspended
        {
            get
            {
                if (Paused) return true;
                suspenders.RemoveWhere(owner => !owner);
                return suspenders.Count > 0;
            }
        }
        /// <summary>Raised when <see cref="Paused"/> changes.</summary>
        public static event Action Changed;

        public static void SetPaused(bool value)
        {
            if (Paused == value) return;
            Paused = value;
            Changed?.Invoke();
        }

        /// <summary>Idempotent claim: a menu in front of the game (the main menu while it is open).</summary>
        public static void Suspend(Object owner, bool suspended)
        {
            if (!owner) return;
            if (suspended) suspenders.Add(owner); else suspenders.Remove(owner);
        }

        public static void ConsumeEscape() => escapeConsumedFrame = Time.frameCount;
        public static bool EscapeConsumed => escapeConsumedFrame == Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            suspenders.Clear();
            Paused = false;
            Changed = null;
            escapeConsumedFrame = -10;
        }
    }
}
