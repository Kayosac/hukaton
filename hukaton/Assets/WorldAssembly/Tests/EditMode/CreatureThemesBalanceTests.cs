using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// Combat balance of the demo (stage 8) in the creature themes: a ring 1 place never sends three bee swarms at once.
    /// </summary>
    public class CreatureThemesBalanceTests
    {
        static IEnumerable<string> Themes()
        {
            var field = typeof(CreatureThemes).GetField("themes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "CreatureThemes keeps its themes in a dictionary");
            return ((IDictionary)field.GetValue(null)).Keys.Cast<string>().ToList();
        }

        /// <summary>Every pick of a theme in a ring: the whole range of options and of "n-m" group sizes.</summary>
        static IEnumerable<List<Spawn>> Picks(string theme, int ring)
        {
            for (int i = 0; i < 2000; i++) yield return CreatureThemes.Pick(theme, ring, (i + .5) / 2000);
        }

        static int Count(List<Spawn> pick, string creature) => pick.Where(s => s.Creature == creature).Sum(s => s.Count);

        [Test]
        public void RingOneSendsAtMostTwoBeeSwarms()
        {
            var themes = Themes().ToList();
            Assert.That(themes, Does.Contain("apiary").And.Contain("watchhill").And.Contain("beeyard").And.Contain("mesa"));
            foreach (var theme in themes)
                foreach (var pick in Picks(theme, 1))
                    Assert.That(Count(pick, "bee-swarm"), Is.LessThanOrEqualTo(2), $"{theme}, ring 1: {string.Join(" + ", pick)}");
            // The apiary still holds two swarms in ring 1, and ring 2 keeps its bigger swarms.
            Assert.That(Picks("apiary", 1).Max(p => Count(p, "bee-swarm")), Is.EqualTo(2));
            Assert.That(Picks("apiary", 2).Max(p => Count(p, "bee-swarm")), Is.EqualTo(4));
            Assert.That(Picks("beeyard", 2).Max(p => Count(p, "bee-swarm")), Is.EqualTo(3));
        }
    }
}
