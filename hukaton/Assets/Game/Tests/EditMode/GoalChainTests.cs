using System;
using System.IO;
using NUnit.Framework;

namespace Hakaton.Game.Tests
{
    /// <summary>Balance stage 6: the goals chain — each step, goals already met passed over, older saves.</summary>
    public sealed class GoalChainTests
    {
        static readonly GoalMarks Marks = GoalMarks.Default;

        static GoalFacts Given(long revenue = 0) => new GoalFacts { given = true, revenue = revenue };

        [Test]
        public void NoGoalBeforeTheTutorialIsBehind()
        {
            Assert.That(GoalChain.Main(new GoalFacts { revenue = 5000 }, Marks), Is.EqualTo(MainGoal.None));
        }

        [Test]
        public void EveryStepOfTheChainInOrder()
        {
            var facts = Given(173);
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.GoblinCard));
            facts.goblinCardBought = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.BringLoot));
            facts.lootBroughtHome = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.FirstRevenue));
            facts.revenue = 403;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.FirstRevenue));
            facts.revenue = 404;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.MeetGod));
            facts.godMet = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.GoblinNews));
            facts.goblinAfterGod = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.SecondRevenue));
            facts.revenue = 807;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.SecondRevenue));
            facts.revenue = 808;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.ThirdRevenue));
            facts.revenue = 1615;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.ThirdRevenue));
            facts.revenue = 1616;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.WorkShift));
            facts.finaleShown = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.LastRevenue));
            facts.revenue = 3231;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.LastRevenue));
            facts.revenue = 3232;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.None), "After the last goal the list is empty.");
        }

        [Test]
        public void GoalsAlreadyMetArePassedOver()
        {
            var facts = Given();
            facts.lootBroughtHome = true;
            facts.goblinCardBought = true;
            facts.godMet = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.FirstRevenue), "The god met early does not pass over 404.");
            facts.revenue = 900;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.GoblinNews));
            facts.goblinAfterGod = true;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.ThirdRevenue), "808 already taken: straight to 1616.");
            facts.revenue = 2000;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.WorkShift), "Past 1616 the end of the demo still waits.");
            facts.revenue = 4000;
            Assert.That(GoalChain.Main(facts, Marks), Is.EqualTo(MainGoal.WorkShift), "The last revenue goal never passes over the end.");
        }

        [Test]
        public void TheEarlyGoalsWaitWhateverTheRevenue()
        {
            // A new game «after the tutorial» starts with goal 1 whatever it was credited.
            Assert.That(GoalChain.Main(Given(5000), Marks), Is.EqualTo(MainGoal.GoblinCard));
        }

        [Test]
        public void TheLinesAsThePlayerReadsThem()
        {
            Assert.That(GoalChain.MainLine(MainGoal.GoblinCard, 0, Marks), Is.EqualTo("Возьми карту у гоблина: дёрни верёвку в подсобке"));
            Assert.That(GoalChain.MainLine(MainGoal.BringLoot, 0, Marks), Is.EqualTo("Принеси добычу со Старой пасеки"));
            Assert.That(GoalChain.MainLine(MainGoal.FirstRevenue, 173, Marks), Is.EqualTo("Выручка: 173/404"));
            Assert.That(GoalChain.MainLine(MainGoal.MeetGod, 0, Marks), Is.EqualTo("Узнай, что за „?“ у двери"));
            Assert.That(GoalChain.MainLine(MainGoal.GoblinNews, 0, Marks), Is.EqualTo("Загляни к гоблину: у него новинки"));
            Assert.That(GoalChain.MainLine(MainGoal.SecondRevenue, 500, Marks), Is.EqualTo("Выручка: 500/808"));
            Assert.That(GoalChain.MainLine(MainGoal.ThirdRevenue, 900, Marks), Is.EqualTo("Выручка: 900/1616"));
            Assert.That(GoalChain.MainLine(MainGoal.WorkShift, 0, Marks), Is.EqualTo("Отработай смену до конца"));
            Assert.That(GoalChain.MainLine(MainGoal.LastRevenue, 2000, Marks), Is.EqualTo("Выручка: 2000/3232"));
            Assert.That(GoalChain.MainLine(MainGoal.None, 0, Marks), Is.Empty);
        }

        [Test]
        public void OnlyTheSecondAndTheLastRevenueGoalsHaveATakenNotice()
        {
            Assert.That(GoalChain.TakenNotice(MainGoal.SecondRevenue, Marks), Is.EqualTo("Выручка 808 взята."));
            Assert.That(GoalChain.TakenNotice(MainGoal.LastRevenue, Marks), Is.EqualTo("Выручка 3232 взята."));
            foreach (MainGoal goal in Enum.GetValues(typeof(MainGoal)))
                if (goal != MainGoal.SecondRevenue && goal != MainGoal.LastRevenue)
                    Assert.That(GoalChain.TakenNotice(goal, Marks), Is.Null, goal.ToString());
        }

        [Test]
        public void TheBannerBelongsToRevenueGoalsOnly()
        {
            foreach (MainGoal goal in Enum.GetValues(typeof(MainGoal)))
            {
                bool revenue = goal == MainGoal.FirstRevenue || goal == MainGoal.SecondRevenue || goal == MainGoal.ThirdRevenue
                    || goal == MainGoal.LastRevenue;
                Assert.That(GoalChain.IsRevenueGoal(goal), Is.EqualTo(revenue), goal.ToString());
            }
            Assert.That(GoalChain.RevenueTarget(MainGoal.ThirdRevenue, new GoalMarks(404, 808, 1700, 3232)), Is.EqualTo(1700));
        }

        static MainGoal MainOf(GoalsSave goals, long revenue, bool godMet) => GoalChain.Main(new GoalFacts
        {
            given = true, revenue = revenue, godMet = godMet, goblinCardBought = goals.goblinCardBought,
            lootBroughtHome = goals.lootBroughtHome, goblinAfterGod = goals.goblinAfterGod, finaleShown = goals.finaleShown
        }, Marks);

        [Test]
        public void OlderSavesAtDifferentStages()
        {
            // Just after the tutorial: nothing bought, nothing brought.
            var fresh = GoalChain.FromOlderSave(null, 173, false, false, false, false);
            Assert.That(MainOf(fresh, 173, false), Is.EqualTo(MainGoal.GoblinCard));
            // Bought at the goblin's, no outing home with goods yet.
            var bought = GoalChain.FromOlderSave(null, 173, false, true, false, false);
            Assert.That(MainOf(bought, 173, false), Is.EqualTo(MainGoal.BringLoot));
            // Goods in the shop after an outing.
            var goods = GoalChain.FromOlderSave(null, 150, false, true, true, false);
            Assert.That(MainOf(goods, 150, false), Is.EqualTo(MainGoal.FirstRevenue));
            // 200 revenue: past goals 1 and 2 whatever the goblin's table says.
            var selling = GoalChain.FromOlderSave(null, 200, false, false, false, false);
            Assert.That(MainOf(selling, 200, false), Is.EqualTo(MainGoal.FirstRevenue));
            // Above 404: never back to goals 1–2.
            var rich = GoalChain.FromOlderSave(null, 450, false, false, false, false);
            Assert.That(MainOf(rich, 450, false), Is.EqualTo(MainGoal.MeetGod));
            // The god met (whatever the revenue): goals 1–2 done; the goblin after him from the items he opened.
            var god = GoalChain.FromOlderSave(null, 120, true, false, false, false);
            Assert.That((god.goblinCardBought, god.lootBroughtHome, god.goblinAfterGod), Is.EqualTo((true, true, false)));
            Assert.That(MainOf(god, 500, true), Is.EqualTo(MainGoal.GoblinNews));
            var news = GoalChain.FromOlderSave(null, 500, true, false, false, true);
            Assert.That(MainOf(news, 500, true), Is.EqualTo(MainGoal.SecondRevenue));
            // Items opened after the god do not count without the god (a cheat, a broken save).
            Assert.That(GoalChain.FromOlderSave(null, 500, false, false, false, true).goblinAfterGod, Is.False);
            // An old save far along: the end of the demo still waits.
            var far = GoalChain.FromOlderSave(null, 2500, true, true, true, true);
            Assert.That(MainOf(far, 2500, true), Is.EqualTo(MainGoal.WorkShift));
            Assert.That(far.version, Is.EqualTo(GoalsSave.Current));
        }

        [Test]
        public void ACurrentRecordIsKeptAsItIs()
        {
            var record = new GoalsSave { version = GoalsSave.Current };
            Assert.That(GoalChain.FromOlderSave(record, 5000, true, true, true, true), Is.SameAs(record),
                "A new game's record (nothing done) is not worked out again from the revenue.");
            Assert.That(MainOf(record, 5000, false), Is.EqualTo(MainGoal.GoblinCard));
        }

        [Test]
        public void TheRecordSurvivesSaveAndLoadAndIsMissingInOlderSaves()
        {
            string directory = Path.Combine(Path.GetTempPath(), "HakatonGoalChainTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "game.json");
                var save = new GameSave { goals = new GoalsSave { version = GoalsSave.Current, goblinCardBought = true, finaleShown = true } };
                Assert.That(GameSaveStore.Save(path, save, out var error), Is.True, error);
                Assert.That(GameSaveStore.Load(path, _ => true, out var loaded, out var warning), Is.True, warning);
                Assert.That((loaded.goals.version, loaded.goals.goblinCardBought, loaded.goals.lootBroughtHome, loaded.goals.finaleShown),
                    Is.EqualTo((GoalsSave.Current, true, false, true)));
                File.WriteAllText(path, "{\"version\":1,\"coins\":0}");
                Assert.That(GameSaveStore.Load(path, _ => true, out var older, out warning), Is.True, warning);
                Assert.That(older.goals == null || older.goals.version == 0, Is.True, "An older save has no goals record.");
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void SideGoalsComeOneAtATimeInTheirWindows()
        {
            var facts = Given();
            Assert.That(GoalChain.Side(MainGoal.GoblinCard, facts), Is.EqualTo(SideGoal.None));
            Assert.That(GoalChain.Side(MainGoal.BringLoot, facts), Is.EqualTo(SideGoal.TrophyCard));
            Assert.That(GoalChain.Side(MainGoal.FirstRevenue, facts), Is.EqualTo(SideGoal.TrophyCard));
            Assert.That(GoalChain.Side(MainGoal.MeetGod, facts), Is.EqualTo(SideGoal.None), "Not done by the end of its window: it just goes.");
            Assert.That(GoalChain.Side(MainGoal.GoblinNews, facts), Is.EqualTo(SideGoal.None));
            Assert.That(GoalChain.Side(MainGoal.SecondRevenue, facts), Is.EqualTo(SideGoal.FullDisplay), "The display first, the walk waits.");
            facts.displayFilled = true;
            Assert.That(GoalChain.Side(MainGoal.SecondRevenue, facts), Is.EqualTo(SideGoal.GoFar));
            Assert.That(GoalChain.Side(MainGoal.ThirdRevenue, facts), Is.EqualTo(SideGoal.GoFar));
            facts.wentFar = true;
            Assert.That(GoalChain.Side(MainGoal.ThirdRevenue, facts), Is.EqualTo(SideGoal.FindAnthill));
            Assert.That(GoalChain.Side(MainGoal.WorkShift, facts), Is.EqualTo(SideGoal.FindAnthill));
            Assert.That(GoalChain.Side(MainGoal.LastRevenue, facts), Is.EqualTo(SideGoal.None));
            facts.anthillFound = true;
            Assert.That(GoalChain.Side(MainGoal.WorkShift, facts), Is.EqualTo(SideGoal.None));
        }

        [Test]
        public void AWindowClosedWithoutTheGoalLetsTheNextOneIn()
        {
            var facts = Given();
            // The display was never filled while 808 was the goal: with 1616 the walk is next.
            Assert.That(GoalChain.Side(MainGoal.ThirdRevenue, facts), Is.EqualTo(SideGoal.GoFar));
            // Never walked 250 m: past 1616 the anthill is next.
            Assert.That(GoalChain.Side(MainGoal.WorkShift, facts), Is.EqualTo(SideGoal.FindAnthill));
            // Done before its window: never shown.
            facts.trophyCardTaken = true;
            Assert.That(GoalChain.Side(MainGoal.BringLoot, facts), Is.EqualTo(SideGoal.None));
            facts.anthillFound = true;
            Assert.That(GoalChain.Side(MainGoal.WorkShift, facts), Is.EqualTo(SideGoal.None));
        }

        [Test]
        public void TheSideLinesAsThePlayerReadsThem()
        {
            Assert.That(GoalChain.SideLine(SideGoal.TrophyCard, 0, 6, 250), Is.EqualTo("+ Добудь трофейную карту: 0/1"));
            Assert.That(GoalChain.SideLine(SideGoal.FullDisplay, 4, 6, 250), Is.EqualTo("+ Заполни витрину: 4/6"));
            Assert.That(GoalChain.SideLine(SideGoal.GoFar, 0, 6, 250), Is.EqualTo("+ Отойди от холма на 250 м"));
            Assert.That(GoalChain.SideLine(SideGoal.FindAnthill, 0, 6, 250), Is.EqualTo("+ Найди Муравейник"));
            Assert.That(GoalChain.SideLine(SideGoal.None, 0, 6, 250), Is.Empty);
        }

        [Test]
        public void TheSideGoalsSurviveSaveAndLoad()
        {
            var record = new GoalsSave { version = GoalsSave.Current, trophyCardTaken = true, displayFilled = false, wentFar = true };
            var copy = UnityEngine.JsonUtility.FromJson<GoalsSave>(UnityEngine.JsonUtility.ToJson(record));
            Assert.That((copy.trophyCardTaken, copy.displayFilled, copy.wentFar), Is.EqualTo((true, false, true)));
        }
    }
}
