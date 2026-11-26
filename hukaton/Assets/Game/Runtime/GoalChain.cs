using System;

namespace Hakaton.Game
{
    /// <summary>The shop's main goals, in the order they come (balance stage 6). None: no goal (before it is given, after the last).</summary>
    public enum MainGoal
    {
        None = 0,
        /// <summary>«Возьми карту у гоблина: дёрни верёвку в подсобке» — a card bought from the goblin.</summary>
        GoblinCard = 1,
        /// <summary>«Принеси добычу со Старой пасеки» — an outing came home with at least one find.</summary>
        BringLoot = 2,
        /// <summary>«Выручка: X/404» — the first revenue goal (its locks stay on <see cref="GameLoop.revenueGoal"/>).</summary>
        FirstRevenue = 3,
        /// <summary>«Узнай, что за „?“ у двери» — the god has been met.</summary>
        MeetGod = 4,
        /// <summary>«Загляни к гоблину: у него новинки» — the goblin called at least once after the god.</summary>
        GoblinNews = 5,
        /// <summary>«Выручка: X/808».</summary>
        SecondRevenue = 6,
        /// <summary>«Выручка: X/1616».</summary>
        ThirdRevenue = 7,
        /// <summary>«Отработай смену до конца» — the end of the demo has been shown.</summary>
        WorkShift = 8,
        /// <summary>«Выручка: X/3232»; after it the goals list is empty.</summary>
        LastRevenue = 9
    }

    /// <summary>The side goal under the main one, one at a time (balance stage 6).</summary>
    public enum SideGoal
    {
        None = 0,
        /// <summary>«+ Добудь трофейную карту: 0/1» — while the main goal is 2 or 3.</summary>
        TrophyCard = 1,
        /// <summary>«+ Заполни витрину: N/6» — while the main goal is 6.</summary>
        FullDisplay = 2,
        /// <summary>«+ Отойди от холма на 250 м» — while the main goal is 6 or 7, after the display.</summary>
        GoFar = 3,
        /// <summary>«+ Найди Муравейник» — while the main goal is 7 or 8, after the 250 m.</summary>
        FindAnthill = 4
    }

    /// <summary>The revenue marks of the chain. The first is the shop's <see cref="GameLoop.revenueGoal"/>.</summary>
    [Serializable]
    public struct GoalMarks
    {
        public long first, second, third, last;
        public GoalMarks(long first, long second, long third, long last) { this.first = first; this.second = second; this.third = third; this.last = last; }
        public static GoalMarks Default => new GoalMarks(404, 808, 1616, 3232);
    }

    /// <summary>What has happened in the game, as far as the goals are concerned.</summary>
    public struct GoalFacts
    {
        /// <summary>The cave and the shop tutorial are behind the player (<see cref="GameLoop.GoalGiven"/>).</summary>
        public bool given;
        public long revenue;
        public bool goblinCardBought, lootBroughtHome, godMet, goblinAfterGod, finaleShown;
        public bool trophyCardTaken, displayFilled, wentFar, anthillFound;
    }

    /// <summary>The goals' own record in the save. Null in saves from before balance stage 6 (see <see cref="GoalChain.FromOlderSave"/>).</summary>
    [Serializable]
    public sealed class GoalsSave
    {
        /// <summary>1 once written by balance stage 6; 0 (or the whole record missing) in older saves.</summary>
        public int version;
        public bool goblinCardBought, lootBroughtHome, goblinAfterGod, finaleShown;
        public bool trophyCardTaken, displayFilled, wentFar;
        public const int Current = 1;
    }

    /// <summary>
    /// The rules of the goals chain, without Unity: which main goal and which side goal the player has now, and their
    /// lines. Goals come in order; one whose condition is already met is passed over.
    /// </summary>
    public static class GoalChain
    {
        public const string GoblinCardLine = "Возьми карту у гоблина: дёрни верёвку в подсобке";
        public const string BringLootLine = "Принеси добычу со Старой пасеки";
        public const string MeetGodLine = "Узнай, что за „?“ у двери";
        public const string GoblinNewsLine = "Загляни к гоблину: у него новинки";
        public const string WorkShiftLine = "Отработай смену до конца";
        public const string TrophyCardLine = "+ Добудь трофейную карту: 0/1";
        public const string GoFarLine = "+ Отойди от холма на 250 м";
        public const string FindAnthillLine = "+ Найди Муравейник";

        /// <summary>Is this goal done?</summary>
        public static bool Done(MainGoal goal, in GoalFacts facts, GoalMarks marks)
        {
            switch (goal)
            {
                case MainGoal.GoblinCard: return facts.goblinCardBought;
                case MainGoal.BringLoot: return facts.lootBroughtHome;
                case MainGoal.FirstRevenue: return facts.revenue >= marks.first;
                case MainGoal.MeetGod: return facts.godMet;
                case MainGoal.GoblinNews: return facts.goblinAfterGod;
                case MainGoal.SecondRevenue: return facts.revenue >= marks.second;
                case MainGoal.ThirdRevenue: return facts.revenue >= marks.third;
                case MainGoal.WorkShift: return facts.finaleShown;
                case MainGoal.LastRevenue: return facts.revenue >= marks.last;
                default: return true;
            }
        }

        /// <summary>The first goal of the chain not done yet; None before the goals are given and after the last.</summary>
        public static MainGoal Main(in GoalFacts facts, GoalMarks marks)
        {
            if (!facts.given) return MainGoal.None;
            for (var goal = MainGoal.GoblinCard; goal <= MainGoal.LastRevenue; goal++)
                if (!Done(goal, facts, marks)) return goal;
            return MainGoal.None;
        }

        /// <summary>The revenue the goal asks for, or 0 when it is not a revenue goal.</summary>
        public static long RevenueTarget(MainGoal goal, GoalMarks marks) =>
            goal == MainGoal.FirstRevenue ? marks.first : goal == MainGoal.SecondRevenue ? marks.second
            : goal == MainGoal.ThirdRevenue ? marks.third : goal == MainGoal.LastRevenue ? marks.last : 0;

        public static bool IsRevenueGoal(MainGoal goal) => RevenueTarget(goal, GoalMarks.Default) > 0;

        /// <summary>The main line as the player reads it.</summary>
        public static string MainLine(MainGoal goal, long revenue, GoalMarks marks)
        {
            switch (goal)
            {
                case MainGoal.GoblinCard: return GoblinCardLine;
                case MainGoal.BringLoot: return BringLootLine;
                case MainGoal.MeetGod: return MeetGodLine;
                case MainGoal.GoblinNews: return GoblinNewsLine;
                case MainGoal.WorkShift: return WorkShiftLine;
                case MainGoal.None: return "";
                default: return "Выручка: " + revenue + "/" + RevenueTarget(goal, marks);
            }
        }

        /// <summary>
        /// The notice when a revenue goal is taken: «Выручка 808 взята.» for the second and the last; null for the others (the
        /// first has its own celebration, the third goes straight on to the next goal).
        /// </summary>
        public static string TakenNotice(MainGoal taken, GoalMarks marks) =>
            taken == MainGoal.SecondRevenue || taken == MainGoal.LastRevenue ? "Выручка " + RevenueTarget(taken, marks) + " взята." : null;

        /// <summary>
        /// The goals' record for a save made before them (<paramref name="save"/> null or version 0), from what the save does
        /// hold. Goals 1 and 2 count as done with <paramref name="revenue"/> ≥ 200 or the god met; otherwise a card bought at the
        /// goblin's table and an outing back with goods in the shop. The goblin after the god: an item he opens after the god
        /// (or later) introduced or bought. A current record is returned as it is.
        /// </summary>
        public static GoalsSave FromOlderSave(GoalsSave save, long revenue, bool godMet, bool goblinSold, bool outingWithGoods, bool afterGodItems)
        {
            if (save != null && save.version >= GoalsSave.Current) return save;
            bool early = revenue >= LegacyEarlyGoalsRevenue || godMet;
            return new GoalsSave
            {
                version = GoalsSave.Current,
                goblinCardBought = early || goblinSold,
                lootBroughtHome = early || outingWithGoods,
                goblinAfterGod = godMet && afterGodItems
            };
        }

        /// <summary>An older save with this much revenue is past goals 1 and 2.</summary>
        public const long LegacyEarlyGoalsRevenue = 200;
    }
}
