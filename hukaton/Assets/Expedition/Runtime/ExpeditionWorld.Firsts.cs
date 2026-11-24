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

        /// <summary>Shown the first time the player comes up to an anthill's mouth (once per game).</summary>
        public const string AnthillLine = "Муравейник. Внутри темно и бьют больно. „Пора домой“ работает и там.";
        /// <summary>Shown at the first hit taken on an outing, not in the cave prologue (once per game).</summary>
        public const string FirstHurtLine = "Упадёшь — половина добычи останется в лесу.";
        const float FirstLineSeconds = 5;

        /// <summary>
        /// The player has come up to an anthill's mouth (<c>DungeonEntrance</c> asks): the first time in the game the line
        /// about the anthill comes, as the outing's other notices do. True when it was shown now.
        /// </summary>
        public bool NoteAnthillApproached()
        {
            if (Firsts.anthillLineShown || !Running || InPrologue) return false;
            Firsts.anthillLineShown = true;
            Announce(AnthillLine, FirstLineSeconds);
            return true;
        }

        // The first hit of the game on an outing (not in the prologue, not the one that ends it): what a defeat costs.
        void NoteHurt(int change)
        {
            if (change >= 0 || Firsts.firstHurtLineShown || !Running || InPrologue || !vitals || vitals.HalfHearts <= 0) return;
            Firsts.firstHurtLineShown = true;
            Announce(FirstHurtLine, FirstLineSeconds);
        }

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
