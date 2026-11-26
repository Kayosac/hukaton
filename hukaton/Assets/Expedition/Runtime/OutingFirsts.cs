using System;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Things that happen once per game on outings, kept in the game's save (<c>GameSave.outingFirsts</c>). Null in older
    /// saves: nothing has happened yet (except what the game loop can tell from older flags).
    /// </summary>
    [Serializable]
    public sealed class OutingFirstsSave
    {
        /// <summary>
        /// The first drowning outside the cave prologue has been forgiven: every find and coin of that outing came home.
        /// From the next one on a drowning costs as any defeat.
        /// </summary>
        public bool drownForgiven;
        /// <summary>The line at an anthill's mouth has been shown (the first time the player came up to one).</summary>
        public bool anthillLineShown;
        /// <summary>The line about the defeat's cost has been shown (the first hit taken on an outing, not in the prologue).</summary>
        public bool firstHurtLineShown;
        /// <summary>The line about ring 1 has been shown (the first time the player went past the calm start, 232 m).</summary>
        public bool wilderRingLineShown;
        /// <summary>The line about mimics has been shown (after the first mimic bite).</summary>
        public bool mimicLineShown;
    }
}
