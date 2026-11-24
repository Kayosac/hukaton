using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Once-per-game moments of the outings (the forgiven first drowning…), saved by the game loop.</summary>
    public sealed partial class ExpeditionWorld
    {
        /// <summary>What has already happened once in this game (live state; the game loop saves and restores a copy).</summary>
        public OutingFirstsSave Firsts { get; private set; } = new OutingFirstsSave();

        /// <summary>The last outing was the first drowning of the game: nothing was lost.</summary>
        public bool LastOutingForgiven { get; private set; }

        /// <summary>A copy of <see cref="Firsts"/> for the save file.</summary>
        public OutingFirstsSave SaveFirsts() => JsonUtility.FromJson<OutingFirstsSave>(JsonUtility.ToJson(Firsts));

        /// <summary>Takes the saved state (a copy); null — a save from before it: nothing has happened yet.</summary>
        public void RestoreFirsts(OutingFirstsSave save)
            => Firsts = save != null ? JsonUtility.FromJson<OutingFirstsSave>(JsonUtility.ToJson(save)) : new OutingFirstsSave();

        /// <summary>
        /// Called as a defeat is counted: true for the game's first drowning (outside the prologue, which never gets here),
        /// which keeps all the loot; marks the forgiveness as used.
        /// </summary>
        bool ForgiveDrowning(bool drowned)
        {
            if (!drowned || Firsts.drownForgiven) return false;
            Firsts.drownForgiven = true;
            return true;
        }
    }
}
