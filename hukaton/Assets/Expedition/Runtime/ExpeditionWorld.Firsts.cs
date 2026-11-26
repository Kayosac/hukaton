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
        {
            Firsts = save != null ? JsonUtility.FromJson<OutingFirstsSave>(JsonUtility.ToJson(save)) : new OutingFirstsSave();
            pendingLine = null;
        }

        /// <summary>Shown the first time the player comes up to an anthill's mouth (once per game).</summary>
        public const string AnthillLine = "Муравейник. Внутри темно и бьют больно. „Пора домой“ работает и там.";
        /// <summary>Shown at the first hit taken on an outing, not in the cave prologue (once per game).</summary>
        public const string FirstHurtLine = "Упадёшь — половина добычи останется в лесу.";
        /// <summary>Shown the first time the player goes past the calm start into the endless world's ring 1 (once per game).</summary>
        public const string WilderRingLine = "Дальше звери злее, а добыча богаче.";
        /// <summary>Shown after the first mimic bite of the game (once per game).</summary>
        public const string MimicLine = "Сундук с зубами. Заморозь или брось карту в закрытый.";
        /// <summary>How long a once-per-game line stays, seconds: no passing notice replaces it meanwhile.</summary>
        public const float FirstLineSeconds = 5;
        /// <summary>A later line that came while another once-per-game line held the screen waits for it this long at most.</summary>
        const float PendingLineSeconds = 10;

        string pendingLine;
        float pendingSince;

        /// <summary>
        /// A mimic has bitten the player (<c>ChestMimic</c> tells): the first time in the game the line about mimics comes.
        /// When the bite was also the game's first hit, its line goes first and this one follows it. True when it was
        /// shown or queued now.
        /// </summary>
        public bool NoteMimicBite()
        {
            if (Firsts.mimicLineShown || pendingLine == MimicLine || !Running || InPrologue) return false;
            ShowFirstLine(MimicLine);
            return true;
        }

        // Every frame of an outing: the first steps past the calm start, and a line waiting for another to go.
        void TickFirsts()
        {
            if (InPrologue) return;
            if (pendingLine != null && Time.time >= noticeHeldUntil)
            {
                string text = pendingLine;
                pendingLine = null;
                if (Time.time - pendingSince <= PendingLineSeconds) ShowFirstLine(text);
            }
            if (!Firsts.wilderRingLineShown && pendingLine != WilderRingLine && activeWorld is IOutingCentre centre)
            {
                float from = centre.WilderFrom;
                if (from > 0 && DistanceFromCentre >= from) ShowFirstLine(WilderRingLine);
            }
        }

        // Shows a once-per-game line and marks it shown; one coming while another holds the screen waits for it.
        void ShowFirstLine(string text)
        {
            if (Time.time < noticeHeldUntil && Notice != text)
            {
                if (pendingLine == null) { pendingLine = text; pendingSince = Time.time; }
                return;
            }
            if (text == WilderRingLine) Firsts.wilderRingLineShown = true;
            else if (text == MimicLine) Firsts.mimicLineShown = true;
            AnnounceHeld(text, FirstLineSeconds);
        }

        /// <summary>
        /// The player has come up to an anthill's mouth (<c>DungeonEntrance</c> asks): the first time in the game the line
        /// about the anthill comes, as the outing's other notices do. True when it was shown now.
        /// </summary>
        public bool NoteAnthillApproached()
        {
            if (Firsts.anthillLineShown || !Running || InPrologue) return false;
            Firsts.anthillLineShown = true;
            AnnounceHeld(AnthillLine, FirstLineSeconds);
            return true;
        }

        // The first hit of the game on an outing (not in the prologue, not the one that ends it): what a defeat costs.
        void NoteHurt(int change)
        {
            if (change >= 0 || Firsts.firstHurtLineShown || !Running || InPrologue || !vitals || vitals.HalfHearts <= 0) return;
            Firsts.firstHurtLineShown = true;
            AnnounceHeld(FirstHurtLine, FirstLineSeconds);
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
