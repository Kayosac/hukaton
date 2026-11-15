using System;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>What «Закрой окно» remembers across saves (kept in <c>GameSave.popups</c>).</summary>
    [Serializable]
    public sealed class PcPopupSave
    {
        [Tooltip("PC openings since the last attack (the first opening of a new game counts as one).")]
        public int opensSinceAttack;
        [Tooltip("Seconds of play since the last attack.")]
        public float secondsSinceAttack;
        [Tooltip("Most ads closed in one round.")]
        public int record;
        [Tooltip("An attack began and has not been won or lost on the screen yet: the next start-up is a blue screen.")]
        public bool attackPending;

        public PcPopupSave Copy() => (PcPopupSave)MemberwiseClone();
    }

    /// <summary>What can be wrong with an ad window (rolled per window, unlocked as the round goes on).</summary>
    [Flags]
    public enum PcPopupTrick
    {
        None = 0,
        /// <summary>The window jumps away when the pointer comes close to its cross (a few times).</summary>
        Runaway = 1,
        /// <summary>Two crosses: the false one brings two more windows.</summary>
        FakeClose = 2,
        /// <summary>Closing asks «Вы уверены?» first.</summary>
        Confirm = 4,
        /// <summary>The window slowly drifts.</summary>
        Creep = 8
    }

    /// <summary>One trick: from which second of the round it may show up and how often.</summary>
    [Serializable]
    public sealed class PcPopupTrickChance
    {
        public PcPopupTrick trick;
        [Min(0)] public float fromSecond;
        [Range(0, 1)] public float chance = .3f;
    }

    /// <summary>The mini-game's tuning (the attack's inspector).</summary>
    [Serializable]
    public sealed class PcPopupSettings
    {
        [Header("When")]
        [Tooltip("Chance per PC opening (one sitting at the PC).")]
        [Range(0, 1)] public float chance = 1f / 6;
        [Tooltip("Seconds of play that must pass after an attack before the next one can come.")]
        [Min(0)] public float cooldownSeconds = 600;
        [Tooltip("Calm openings needed between two attacks (1: never two openings in a row; also: never at the very first opening).")]
        [Min(0)] public int calmOpeningsBetween = 1;
        [Tooltip("The attack starts this long after the desktop is up.")]
        [Min(0)] public float startDelayMin = 2, startDelayMax = 4;

        [Header("Round")]
        [Min(1)] public float roundSeconds = 15;
        [Tooltip("This many windows open at once: lost.")]
        [Min(1)] public int loseAt = 12;
        [Min(0)] public int startWindows = 2;
        [Tooltip("Seconds between new windows at the start and at the end of the round.")]
        [Min(.05f)] public float intervalStart = 1f, intervalEnd = .35f;
        [Tooltip("How the interval goes from start to end: 1 evenly, above 1 slow at first and fast later.")]
        [Min(.1f)] public float intervalPower = 1.4f;

        [Header("Tricks")]
        public PcPopupTrickChance[] tricks =
        {
            new PcPopupTrickChance { trick = PcPopupTrick.Runaway, fromSecond = 2.5f, chance = .3f },
            new PcPopupTrickChance { trick = PcPopupTrick.FakeClose, fromSecond = 4.5f, chance = .3f },
            new PcPopupTrickChance { trick = PcPopupTrick.Confirm, fromSecond = 6.5f, chance = .3f },
            new PcPopupTrickChance { trick = PcPopupTrick.Creep, fromSecond = 8.5f, chance = .35f },
        };
        [Tooltip("At most this many tricks on one window.")]
        [Min(0)] public int maxTricksPerWindow = 1;
        [Tooltip("A runaway window jumps when the pointer is this close (PC pixels) to its cross.")]
        [Min(1)] public float dodgeRadius = 16;
        [Min(0)] public int dodgeTimes = 2;
        [Tooltip("Windows brought by the false cross.")]
        [Min(0)] public int fakeSpawns = 2;
        [Tooltip("A creeping window's drift, PC pixels per second.")]
        [Min(0)] public float creepSpeed = 14;
        [Tooltip("«Да» and «Нет» of the confirmation swap places with this chance.")]
        [Range(0, 1)] public float confirmSwapChance = .4f;

        [Header("Outcome")]
        [Min(0)] public int reward = 40;
        [Tooltip("The virus takes this share of the balance (rounded down, at least 1 coin if there are any)...")]
        [Range(0, 1)] public float penaltyShare = .1f;
        [Tooltip("...but never more than this.")]
        [Min(0)] public int penaltyMax = 150;
        [Tooltip("The blue screen stays this long before the PC restarts.")]
        [Min(.1f)] public float crashSeconds = 2.5f;
    }

    public enum PcPopupVerdict
    {
        /// <summary>Something is on (a guide, the shop tutorial, a merge...): no roll at all.</summary>
        Blocked,
        /// <summary>Too soon after the last attack (time or openings).</summary>
        NotYet,
        /// <summary>The chance said no.</summary>
        Missed,
        Attack
    }

    /// <summary>The mini-game's plain rules: when an attack comes, how fast windows come, tricks, the virus's take.</summary>
    public static class PcPopupRules
    {
        /// <summary>
        /// At a PC opening. <paramref name="clear"/>: nothing forbids it now (guides, the tutorial, a merge or drag).
        /// <paramref name="forced"/> (the cheat): no chance and no cooldowns, but still nothing while something is on.
        /// </summary>
        public static PcPopupVerdict Roll(PcPopupSave save, PcPopupSettings settings, bool clear, bool forced, double roll01)
        {
            if (!clear) return PcPopupVerdict.Blocked;
            if (forced) return PcPopupVerdict.Attack;
            if (save.attackPending) return PcPopupVerdict.Blocked;
            if (save.opensSinceAttack < settings.calmOpeningsBetween || save.secondsSinceAttack < settings.cooldownSeconds)
                return PcPopupVerdict.NotYet;
            return roll01 < settings.chance ? PcPopupVerdict.Attack : PcPopupVerdict.Missed;
        }

        /// <summary>An opening that passed without an attack.</summary>
        public static void Calm(PcPopupSave save) => save.opensSinceAttack++;

        /// <summary>The attack has started: the cooldowns begin again and leaving now loses it.</summary>
        public static void Started(PcPopupSave save)
        {
            save.opensSinceAttack = 0;
            save.secondsSinceAttack = 0;
            save.attackPending = true;
        }

        public static void CountPlayTime(PcPopupSave save, float seconds)
        {
            if (seconds > 0 && !float.IsNaN(seconds) && !float.IsInfinity(seconds))
                save.secondsSinceAttack = Mathf.Min(save.secondsSinceAttack + seconds, 1e7f);
        }

        /// <summary>Won: the record, no blue screen. True when the record is new.</summary>
        public static bool Won(PcPopupSave save, int closed)
        {
            save.attackPending = false;
            if (closed <= save.record) return false;
            save.record = closed;
            return true;
        }

        /// <summary>Seconds until the next window at <paramref name="elapsed"/> seconds into the round.</summary>
        public static float Interval(float elapsed, PcPopupSettings settings)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.01f, settings.roundSeconds));
            return Mathf.Lerp(settings.intervalStart, settings.intervalEnd, Mathf.Pow(t, settings.intervalPower));
        }

        public static bool Lost(int openWindows, PcPopupSettings settings) => openWindows >= settings.loseAt;

        /// <summary>The virus's take: a share of the balance rounded down, at least 1 coin when there are any, capped.</summary>
        public static int Penalty(int balance, PcPopupSettings settings)
        {
            if (balance <= 0) return 0;
            int share = (int)Math.Floor(balance * (double)settings.penaltyShare);
            return Mathf.Clamp(Math.Min(share, settings.penaltyMax), 1, balance);
        }

        /// <summary>The tricks open at <paramref name="elapsed"/> seconds into the round.</summary>
        public static PcPopupTrick Available(float elapsed, PcPopupSettings settings)
        {
            var open = PcPopupTrick.None;
            if (settings.tricks == null) return open;
            foreach (var entry in settings.tricks)
                if (entry != null && entry.chance > 0 && elapsed >= entry.fromSecond) open |= entry.trick;
            return open;
        }

        /// <summary>Rolls the tricks of a new window (each open trick by its chance, at most the cap, in list order).</summary>
        public static PcPopupTrick Pick(float elapsed, PcPopupSettings settings, Func<double> random01)
        {
            var picked = PcPopupTrick.None;
            if (settings.tricks == null || settings.maxTricksPerWindow <= 0) return picked;
            int count = 0;
            // A random starting point, so the first entries are not favoured under the cap.
            int n = settings.tricks.Length, start = n > 0 ? (int)(random01() * n) % n : 0;
            for (int i = 0; i < n && count < settings.maxTricksPerWindow; i++)
            {
                var entry = settings.tricks[(start + i) % n];
                if (entry == null || elapsed < entry.fromSecond || (picked & entry.trick) != 0) continue;
                if (random01() < entry.chance) { picked |= entry.trick; count++; }
            }
            return picked;
        }
    
        /// <summary>
        /// Fills {key} with the number, and {key|одна|две|пять} with the number and the word agreeing with it
        /// («23 окна», «17 монет», «21 монету»).
        /// </summary>
        public static string Fill(string line, string key, int value)
        {
            if (string.IsNullOrEmpty(line)) return line;
            line = line.Replace("{" + key + "}", value.ToString());
            string open = "{" + key + "|";
            for (int at = line.IndexOf(open, StringComparison.Ordinal); at >= 0; at = line.IndexOf(open, StringComparison.Ordinal))
            {
                int end = line.IndexOf('}', at);
                if (end < 0) break;
                var forms = line.Substring(at + open.Length, end - at - open.Length).Split('|');
                string word = forms.Length == 3 ? forms[Plural(value)] : forms[forms.Length - 1];
                line = line.Substring(0, at) + value + " " + word + line.Substring(end + 1);
            }
            return line;
        }

        /// <summary>Russian plural form: 0 — 1 (21, 31…), 1 — 2–4 (22…), 2 — 5–20 and the rest.</summary>
        public static int Plural(int n)
        {
            int last = Math.Abs(n) % 10, lastTwo = Math.Abs(n) % 100;
            return last == 1 && lastTwo != 11 ? 0 : last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14) ? 1 : 2;
        }
    }
}
