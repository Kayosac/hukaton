using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Кривое зеркало»: damage, mass and growth rules, the crooked look, the card's numbers and assets.</summary>
    public sealed class CrookedMirrorTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void ShrunkCreatureDealsItsShareButNeverLessThanHalfAHeart()
        {
            Assert.That(CrookedMirrorRules.Damage(4, .5f), Is.EqualTo(2));
            Assert.That(CrookedMirrorRules.Damage(6, .3f), Is.EqualTo(2));
            Assert.That(CrookedMirrorRules.Damage(5, .4f), Is.EqualTo(2));
            Assert.That(CrookedMirrorRules.Damage(2, .4f), Is.EqualTo(1), "0.8 rounds up to the minimum");
            Assert.That(CrookedMirrorRules.Damage(1, .3f), Is.EqualTo(1), "never below ½ heart");
            Assert.That(CrookedMirrorRules.Damage(3, .3f), Is.EqualTo(1));
            Assert.That(CrookedMirrorRules.Damage(0, .5f), Is.EqualTo(0), "a harmless hit stays harmless");
            Assert.That(CrookedMirrorRules.Damage(5, 1), Is.EqualTo(5), "normal size: unchanged");
            Assert.That(WorldCreature.ScaledDamage(7, 1), Is.EqualTo(7));
        }

        [Test]
        public void LighterBodyFliesFurtherFromTheSameImpulse()
        {
            Assert.That(CrookedMirrorRules.Mass(2, .5f), Is.EqualTo(1).Within(1e-5f));
            Assert.That(CrookedMirrorRules.Mass(3, .3f), Is.EqualTo(.9f).Within(1e-5f));
            float impulse = 8;
            float normal = impulse / 2, shrunk = impulse / CrookedMirrorRules.Mass(2, .4f);
            Assert.That(shrunk / normal, Is.EqualTo(2.5f).Within(1e-4f), "Δv = I / m: a 40 % creature flies 2.5 times faster");
            Assert.That(CrookedMirrorRules.Mass(2, 3), Is.EqualTo(2).Within(1e-5f), "the mirror never makes it heavier");
        }

        [Test]
        public void GrowthOfACubeOrMushroomDoesNotStack()
        {
            Assert.That(CrookedMirrorRules.Growth(1, 1.5f), Is.EqualTo(1.5f));
            Assert.That(CrookedMirrorRules.Growth(1.5f, 1.5f), Is.EqualTo(1.5f), "the same copy again: no bigger");
            Assert.That(CrookedMirrorRules.Growth(1.75f, 1.5f), Is.EqualTo(1.75f), "a weaker copy never shrinks it");
            Assert.That(CrookedMirrorRules.Growth(1.5f, 2), Is.EqualTo(2), "a stronger copy grows it further");
            Assert.That(CrookedMirrorRules.Growth(1, .5f), Is.EqualTo(1), "never smaller than it was");
        }

        [Test]
        public void SecondMirrorKeepsTheLaterEnd()
        {
            Assert.That(CrookedMirrorRules.Until(20, 10, 12), Is.EqualTo(22));
            Assert.That(CrookedMirrorRules.Until(30, 10, 12), Is.EqualTo(30), "a shorter copy does not cut it short");
            Assert.That(CrookedMirrorRules.Until(5, 10, 12), Is.EqualTo(22), "an ended one starts afresh");
        }

        [Test]
        public void CrookedLookSettlesIntoASteadySkewAndLean()
        {
            var early = CrookedMirrorRules.Crook(.05f, 1, .16f, 8, 1.4f);
            var settled = CrookedMirrorRules.Crook(30, 1, .16f, 8, 1.4f);
            Assert.That(settled.x, Is.GreaterThan(1), "stretched across");
            Assert.That(settled.y, Is.LessThan(1), "squeezed down");
            Assert.That(settled.z, Is.GreaterThan(0), "leans to its side");
            var mirrored = CrookedMirrorRules.Crook(30, 1, .16f, -8, 1.4f);
            Assert.That(mirrored.z, Is.LessThan(0));
            var straight = CrookedMirrorRules.Crook(30, 1, 0, 0, 1.4f);
            Assert.That(Vector3.Distance(straight, new Vector3(1, 1, 0)), Is.LessThan(1e-5f), "no skew, no lean: the drawing as it is");
            Assert.That(Mathf.Abs(early.x - settled.x) + Mathf.Abs(early.z - settled.z), Is.GreaterThan(0), "it wobbles right after the change");
        }

        [Test]
        public void CardNumbersAndAssetsAreWired()
        {
            var effect = AssetDatabase.LoadAssetAtPath<CrookedMirrorEffect>("Assets/Game/Data/Effects/Crooked mirror.asset");
            Assert.That(effect, Is.Not.Null);
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/crooked_mirror.asset");
            Assert.That(card.effectAsset, Is.SameAs(effect));
            Assert.That((effect.shrink.i, effect.shrink.ii, effect.shrink.iii), Is.EqualTo((.5f, .4f, .3f)));
            Assert.That((effect.duration.i, effect.duration.ii, effect.duration.iii), Is.EqualTo((12f, 16f, 20f)));
            Assert.That((effect.growth.i, effect.growth.ii, effect.growth.iii), Is.EqualTo((1.5f, 1.75f, 2f)));
            Assert.That(effect.TryGetStat(out string label, out var values, out string unit), Is.True);
            Assert.That((label, values.i, values.ii, values.iii, unit), Is.EqualTo(("Размер зверя", 50f, 40f, 30f, " %")));
            Assert.That(effect.impactVfx, Is.Not.Null, "shrink burst");
            Assert.That(effect.impactSound, Is.Not.Null);
            Assert.That(effect.missSound, Is.Not.Null, "a fizzle on anything else");
            Assert.That(effect.growVfx, Is.Not.Null);
            Assert.That(effect.look.aura, Is.Not.Null);
            Assert.That(effect.look.restoreVfx, Is.Not.Null);
        }

        [Test]
        public void StoneCubeGrowsItsColliderAndKeepsItsRootUnscaled()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<StoneCubeBody>("Assets/Game/Prefabs/Cards/StoneCube/Stone cube.prefab");
            Assume.That(prefab, Is.Not.Null);
            var cube = Object.Instantiate(prefab);
            created.Add(cube.gameObject);
            cube.box.size = Vector3.one * 1.1f;
            var growth = CrookedMirrorGrowth.On(cube.gameObject);
            Assert.That(growth.Grow(cube, null, 1.5f, .8f), Is.True);
            Assert.That(growth.Grow(cube, null, 1.5f, .8f), Is.False, "the same multiplier again does nothing");
            Assert.That(growth.Grow(cube, null, 2, .8f), Is.True);
            Assert.That(growth.Factor, Is.EqualTo(2));
            Assert.That(CrookedMirrorGrowth.On(cube.gameObject), Is.SameAs(growth), "one growth per object");
            Assert.That(cube.transform.localScale, Is.EqualTo(Vector3.one), "the root stays unscaled");
        }
    }
}
