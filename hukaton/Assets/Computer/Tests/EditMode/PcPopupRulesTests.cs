using System.Linq;
using NUnit.Framework;

namespace Hakaton.Computer.Tests
{
    public sealed class PcPopupRulesTests
    {
        static PcPopupSave Ready() => new PcPopupSave { opensSinceAttack = 1, secondsSinceAttack = 601 };

        [Test]
        public void AttackComesOnlyByTheChanceOnceTheCooldownsHavePassed()
        {
            var s = new PcPopupSettings();
            Assert.That(s.chance, Is.EqualTo(1f / 6).Within(1e-6));
            Assert.That(PcPopupRules.Roll(Ready(), s, true, false, .1), Is.EqualTo(PcPopupVerdict.Attack));
            Assert.That(PcPopupRules.Roll(Ready(), s, true, false, .2), Is.EqualTo(PcPopupVerdict.Missed));
        }

        [Test]
        public void NothingIsRolledWhileSomethingIsOnEvenWhenForced()
        {
            var s = new PcPopupSettings();
            Assert.That(PcPopupRules.Roll(Ready(), s, false, false, 0), Is.EqualTo(PcPopupVerdict.Blocked));
            Assert.That(PcPopupRules.Roll(Ready(), s, false, true, 0), Is.EqualTo(PcPopupVerdict.Blocked));
        }

        [Test]
        public void TenMinutesAndACalmOpeningLieBetweenAttacks()
        {
            var s = new PcPopupSettings();
            var save = Ready();
            PcPopupRules.Started(save);
            Assert.That(save.attackPending, Is.True);
            Assert.That(PcPopupRules.Won(save, 5), Is.True);
            // Right after: neither the time nor a calm opening yet.
            Assert.That(PcPopupRules.Roll(save, s, true, false, 0), Is.EqualTo(PcPopupVerdict.NotYet));
            PcPopupRules.CountPlayTime(save, 700);
            Assert.That(PcPopupRules.Roll(save, s, true, false, 0), Is.EqualTo(PcPopupVerdict.NotYet), "Never two openings in a row.");
            PcPopupRules.Calm(save);
            Assert.That(PcPopupRules.Roll(save, s, true, false, 0), Is.EqualTo(PcPopupVerdict.Attack));
            // A calm opening is not enough without the time.
            var early = new PcPopupSave { opensSinceAttack = 3, secondsSinceAttack = 599 };
            Assert.That(PcPopupRules.Roll(early, s, true, false, 0), Is.EqualTo(PcPopupVerdict.NotYet));
        }

        [Test]
        public void ANewOrOldSaveNeverAttacksAtTheFirstOpening()
        {
            var s = new PcPopupSettings();
            var fresh = new PcPopupSave();
            PcPopupRules.CountPlayTime(fresh, 10000);
            Assert.That(PcPopupRules.Roll(fresh, s, true, false, 0), Is.EqualTo(PcPopupVerdict.NotYet));
            PcPopupRules.Calm(fresh);
            Assert.That(PcPopupRules.Roll(fresh, s, true, false, 0), Is.EqualTo(PcPopupVerdict.Attack));
        }

        [Test]
        public void TheCheatSkipsTheChanceAndTheCooldowns()
        {
            var s = new PcPopupSettings();
            Assert.That(PcPopupRules.Roll(new PcPopupSave(), s, true, true, .99), Is.EqualTo(PcPopupVerdict.Attack));
        }

        [Test]
        public void PlayTimeIgnoresNonsenseAndTheRecordOnlyGrows()
        {
            var save = new PcPopupSave();
            PcPopupRules.CountPlayTime(save, float.NaN);
            PcPopupRules.CountPlayTime(save, -5);
            PcPopupRules.CountPlayTime(save, 2.5f);
            Assert.That(save.secondsSinceAttack, Is.EqualTo(2.5f));
            Assert.That(PcPopupRules.Won(save, 7), Is.True);
            Assert.That(PcPopupRules.Won(save, 4), Is.False);
            Assert.That(save.record, Is.EqualTo(7));
        }

        [Test]
        public void WindowsComeFasterAsTheRoundGoesOn()
        {
            var s = new PcPopupSettings();
            Assert.That(PcPopupRules.Interval(0, s), Is.EqualTo(1f).Within(1e-4));
            Assert.That(PcPopupRules.Interval(s.roundSeconds, s), Is.EqualTo(.35f).Within(1e-4));
            Assert.That(PcPopupRules.Interval(99, s), Is.EqualTo(.35f).Within(1e-4));
            float last = float.MaxValue;
            for (float t = 0; t <= s.roundSeconds; t += .5f)
            {
                float interval = PcPopupRules.Interval(t, s);
                Assert.That(interval, Is.LessThanOrEqualTo(last));
                last = interval;
            }
            // Without closing anything, the screen fills up before the round ends.
            int open = s.startWindows;
            float next = PcPopupRules.Interval(0, s);
            while (next < s.roundSeconds) { open++; next += PcPopupRules.Interval(next, s); }
            Assert.That(PcPopupRules.Lost(open, s), Is.True);
            Assert.That(PcPopupRules.Lost(s.loseAt - 1, s), Is.False);
        }

        [Test]
        public void TheVirusTakesATenthRoundedDownAtLeastOneAtMostAHundredAndFifty()
        {
            var s = new PcPopupSettings();
            Assert.That(PcPopupRules.Penalty(0, s), Is.Zero);
            Assert.That(PcPopupRules.Penalty(-3, s), Is.Zero);
            Assert.That(PcPopupRules.Penalty(1, s), Is.EqualTo(1));
            Assert.That(PcPopupRules.Penalty(9, s), Is.EqualTo(1));
            Assert.That(PcPopupRules.Penalty(19, s), Is.EqualTo(1));
            Assert.That(PcPopupRules.Penalty(437, s), Is.EqualTo(43));
            Assert.That(PcPopupRules.Penalty(1500, s), Is.EqualTo(150));
            Assert.That(PcPopupRules.Penalty(1000000, s), Is.EqualTo(150));
        }

        [Test]
        public void TricksOpenWithTimeAndStayUnderTheCap()
        {
            var s = new PcPopupSettings();
            Assert.That(PcPopupRules.Available(0, s), Is.EqualTo(PcPopupTrick.None));
            Assert.That(PcPopupRules.Pick(0, s, () => 0), Is.EqualTo(PcPopupTrick.None));
            Assert.That(PcPopupRules.Available(5, s), Is.EqualTo(PcPopupTrick.Runaway | PcPopupTrick.FakeClose));
            Assert.That(PcPopupRules.Available(15, s), Is.EqualTo(PcPopupTrick.Runaway | PcPopupTrick.FakeClose | PcPopupTrick.Confirm | PcPopupTrick.Creep));
            // Every roll a hit: still only one trick per window.
            var picked = PcPopupRules.Pick(15, s, () => 0);
            Assert.That(System.Enum.GetValues(typeof(PcPopupTrick)).Cast<PcPopupTrick>().Count(t => t != 0 && (picked & t) != 0), Is.EqualTo(1));
            // Every roll a miss: none.
            Assert.That(PcPopupRules.Pick(15, s, () => .999), Is.EqualTo(PcPopupTrick.None));
            s.maxTricksPerWindow = 4;
            Assert.That(PcPopupRules.Pick(15, s, () => 0), Is.EqualTo(PcPopupRules.Available(15, s)));
            // A trick picked early can only be one that is open.
            var random = new System.Random(3);
            for (int i = 0; i < 200; i++)
                Assert.That(PcPopupRules.Pick(3, s, random.NextDouble) & ~PcPopupTrick.Runaway, Is.EqualTo(PcPopupTrick.None));
        }
    
        [TestCase(1, "1 окно")] [TestCase(3, "3 окна")] [TestCase(11, "11 окон")] [TestCase(21, "21 окно")]
        [TestCase(23, "23 окна")] [TestCase(25, "25 окон")] [TestCase(112, "112 окон")] [TestCase(0, "0 окон")]
        public void LinesAgreeTheWordWithTheNumber(int n, string expected)
        {
            Assert.That(PcPopupRules.Fill("Закрыто {closed|окно|окна|окон}!", "closed", n), Is.EqualTo("Закрыто " + expected + "!"));
            Assert.That(PcPopupRules.Fill("Рекорд: {closed}", "closed", n), Is.EqualTo("Рекорд: " + n));
        }
    
        [Test]
        public void TenClosedAdsWinAtOnceAndHoldingOutStillWins()
        {
            var s = new PcPopupSettings();
            Assert.That(s.closeToWin, Is.EqualTo(10));
            Assert.That(PcPopupRules.RoundWon(9, 3, s), Is.False);
            Assert.That(PcPopupRules.RoundWon(10, 3, s), Is.True, "Ten closed: won, however early.");
            Assert.That(PcPopupRules.RoundWon(4, s.roundSeconds, s), Is.True, "Held out to the end: won too.");
        }

        [Test]
        public void TheWinSweepSpeedsUpAndRisesInPitch()
        {
            var s = new PcPopupSettings();
            Assert.That(PcPopupRules.SweepStep(0, 8, s), Is.EqualTo(s.sweepStepStart).Within(1e-5));
            Assert.That(PcPopupRules.SweepStep(7, 8, s), Is.EqualTo(s.sweepStepEnd).Within(1e-5));
            Assert.That(PcPopupRules.SweepStep(3, 8, s), Is.LessThan(PcPopupRules.SweepStep(2, 8, s)));
            Assert.That(PcPopupRules.SweepPitch(0, s), Is.EqualTo(1).Within(1e-5));
            Assert.That(PcPopupRules.SweepPitch(5, s), Is.GreaterThan(PcPopupRules.SweepPitch(4, s)));
            Assert.That(PcPopupRules.SweepPitch(100, s), Is.EqualTo(s.sweepPitchMax).Within(1e-5));
        }
    }
}
