using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Оживление дерева»: the branch strike cycle, whom the tree fights, when creatures turn on it, the assets.</summary>
    public sealed class TreeAwakeningTests
    {
        const float Cycle = 2.5f, Windup = .8f, Window = .3f;

        // ---------- attack cycle ----------

        [Test]
        public void StrikeCycleRunsWindupStrikeRecoveryInTwoAndAHalfSeconds()
        {
            var cycle = new TreeAttackCycle(Cycle, Windup, Window);
            Assert.That(cycle.Ready(0), Is.True);
            Assert.That(cycle.Begin(10), Is.True);
            Assert.That(cycle.Advance(10.1f), Is.EqualTo(TreeAttackPhase.Windup));
            Assert.That(cycle.Advance(10.79f), Is.EqualTo(TreeAttackPhase.Windup), "Board*: a 0.8 s wind-up.");
            Assert.That(cycle.Advance(10.81f), Is.EqualTo(TreeAttackPhase.Strike));
            Assert.That(cycle.Advance(11.2f), Is.EqualTo(TreeAttackPhase.Recover));
            Assert.That(cycle.Advance(12.49f), Is.EqualTo(TreeAttackPhase.Recover));
            Assert.That(cycle.Advance(12.51f), Is.EqualTo(TreeAttackPhase.Ready), "Board*: the whole cycle is 2.5 s.");
        }

        [Test]
        public void NewStrikeCannotStartBeforeTheCycleEnds()
        {
            var cycle = new TreeAttackCycle(Cycle, Windup, Window);
            cycle.Begin(0);
            Assert.That(cycle.Begin(1), Is.False);
            Assert.That(cycle.Begin(2.4f), Is.False);
            Assert.That(cycle.Begin(2.6f), Is.True);
            Assert.That(cycle.StartedAt, Is.EqualTo(2.6f));
        }

        [Test]
        public void BranchLandsOnlyDuringTheStrikeAndAtMostOnce()
        {
            var cycle = new TreeAttackCycle(Cycle, Windup, Window);
            cycle.Begin(0);
            Assert.That(cycle.TryLand(.5f), Is.False, "No damage during the wind-up.");
            Assert.That(cycle.TryLand(.9f), Is.True, "Contact during the strike deals damage.");
            Assert.That(cycle.TryLand(.95f), Is.False, "Not twice in one strike.");
            Assert.That(cycle.Landed, Is.True);
            cycle.Begin(3);
            Assert.That(cycle.Landed, Is.False);
            Assert.That(cycle.TryLand(4.5f), Is.False, "Too late: the branch has already passed (a missed strike).");
            Assert.That(cycle.TryLand(5.6f), Is.False);
        }

        [Test]
        public void CycleNeverShorterThanWindupAndStrike()
        {
            var cycle = new TreeAttackCycle(.5f, .8f, .3f);
            Assert.That(cycle.Cycle, Is.EqualTo(1.1f).Within(1e-5f));
            cycle.Begin(0);
            Assert.That(cycle.WindupProgress(.4f), Is.EqualTo(.5f).Within(1e-5f));
        }

        // ---------- whom the tree fights ----------

        static readonly TreeAllyTargeting.Rules Rules = TreeAllyTargeting.Rules.Default;

        static FoeCandidate Foe(float toAlly, float toPlayer, bool attacksSquad = false, bool attacksTree = false,
            bool hurt = false, bool current = false) => new FoeCandidate
        {
            DistanceToAlly = toAlly, DistanceToPlayer = toPlayer, AttacksSquad = attacksSquad || attacksTree,
            AttacksThisAlly = attacksTree, RecentlyHurt = hurt, Current = current,
        };

        [Test]
        public void PeacefulCreaturesAreLeftAlone()
        {
            var list = new List<FoeCandidate> { Foe(1, 2), Foe(3, 3) };
            Assert.That(TreeAllyTargeting.Choose(list, Rules, 2), Is.EqualTo(-1), "The tree never hunts peaceful creatures.");
        }

        [Test]
        public void HelpsAgainstAttackersAndTheCreatureThePlayerHurt()
        {
            var attacker = new List<FoeCandidate> { Foe(2, 5), Foe(6, 2, attacksSquad: true) };
            Assert.That(TreeAllyTargeting.Choose(attacker, Rules, 3), Is.EqualTo(1));
            var hurt = new List<FoeCandidate> { Foe(2, 5), Foe(5, 6, hurt: true) };
            Assert.That(TreeAllyTargeting.Choose(hurt, Rules, 3), Is.EqualTo(1), "The one the player attacked.");
        }

        [Test]
        public void PrefersTheCreatureAttackingTheTreeAndKeepsItsFoe()
        {
            var list = new List<FoeCandidate> { Foe(3, 3, attacksSquad: true), Foe(4.5f, 6, attacksTree: true) };
            Assert.That(TreeAllyTargeting.Choose(list, Rules, 3), Is.EqualTo(1), "Self-defence wins over a slightly nearer attacker.");
            var sticky = new List<FoeCandidate> { Foe(3, 3, attacksSquad: true), Foe(4, 4, attacksSquad: true, current: true) };
            Assert.That(TreeAllyTargeting.Choose(sticky, Rules, 3), Is.EqualTo(1), "Does not flip to a foe that is only a bit closer.");
        }

        [Test]
        public void StaysWithThePlayerButAlwaysDefendsItself()
        {
            var farFromPlayer = new List<FoeCandidate> { Foe(3, Rules.assistRadius + 5, attacksSquad: true) };
            Assert.That(TreeAllyTargeting.Choose(farFromPlayer, Rules, 4), Is.EqualTo(-1), "Does not wander off after a far fight.");
            var tooFar = new List<FoeCandidate> { Foe(Rules.engageRange + 1, 2, attacksSquad: true) };
            Assert.That(TreeAllyTargeting.Choose(tooFar, Rules, 3), Is.EqualTo(-1));
            var leashed = new List<FoeCandidate> { Foe(2, 3, attacksSquad: true), Foe(4, 30, attacksTree: true) };
            Assert.That(TreeAllyTargeting.Choose(leashed, Rules, Rules.leashFromPlayer + 10), Is.EqualTo(1),
                "Left behind (no path to the player), it still fights back.");
        }

        // ---------- creatures turn on the tree ----------

        [Test]
        public void AttackersTurnOnTheTreeWhenItIsClearlyCloser()
        {
            Assert.That(TreeAllyTargeting.Taunt(false, false, false, 2, 6, 6, 6, 1, 3), Is.EqualTo(TauntDecision.Draw));
            Assert.That(TreeAllyTargeting.Taunt(false, false, false, 5.5f, 6, 6, 6, 1, 3), Is.EqualTo(TauntDecision.Keep), "Not by a hair.");
            Assert.That(TreeAllyTargeting.Taunt(false, false, false, 7, 12, 12, 6, 1, 3), Is.EqualTo(TauntDecision.Keep), "Out of range.");
            Assert.That(TreeAllyTargeting.Taunt(false, true, false, 1, 6, 6, 6, 1, 3), Is.EqualTo(TauntDecision.Keep),
                "An attack already winding up is never redirected.");
        }

        [Test]
        public void AttackersGoBackToThePlayerUnlessTheTreeKeepsHittingThem()
        {
            Assert.That(TreeAllyTargeting.Taunt(true, false, false, 8, 8, 2, 6, 1, 3), Is.EqualTo(TauntDecision.Release));
            Assert.That(TreeAllyTargeting.Taunt(true, false, true, 8, 8, 2, 6, 1, 3), Is.EqualTo(TauntDecision.Keep), "The tree hit it lately.");
            Assert.That(TreeAllyTargeting.Taunt(true, false, false, 3, 3, 2, 6, 1, 3), Is.EqualTo(TauntDecision.Keep));
        }

        // ---------- assets ----------

        [Test]
        public void CardAssetCarriesTheBoardNumbersAndTheAlly()
        {
            var definition = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/tree_awakening.asset");
            Assert.That(definition, Is.Not.Null);
            var effect = definition.effectAsset as TreeAwakeningEffect;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.healthBase, Is.EqualTo(12));
            Assert.That(effect.healthUpgraded, Is.EqualTo(18));
            Assert.That(effect.damage, Is.EqualTo(2));
            Assert.That(effect.reach, Is.EqualTo(2.5f));
            Assert.That(effect.attackCycle, Is.EqualTo(2.5f));
            Assert.That(effect.windup, Is.EqualTo(.8f));
            Assert.That(effect.mass, Is.EqualTo(3));
            Assert.That(effect.allyPrefab, Is.Not.Null, "The effect spawns the authored ally prefab.");
        }

        [Test]
        public void AllyPrefabIsWiredAndItsLookIsASwappableChildPrefab()
        {
            var effect = AssetDatabase.LoadAssetAtPath<TreeAwakeningEffect>("Assets/Game/Data/Effects/Tree awakening.asset");
            var ally = effect.allyPrefab;
            Assert.That(ally.body, Is.Not.Null);
            Assert.That(ally.navigator, Is.Not.Null);
            Assert.That(ally.navigator.enabled, Is.False, "The agent is enabled once the tree stands on the NavMesh.");
            Assert.That(ally.overhead, Is.Not.Null);
            Assert.That(ally.look, Is.Not.Null);
            Assert.That(ally.look.transform.IsChildOf(ally.transform), Is.True);
            Assert.That(PrefabUtility.IsAnyPrefabInstanceRoot(ally.look.gameObject), Is.True,
                "The placeholder look is its own nested prefab, replaceable by the owner's art.");
            Assert.That(ally.GetComponentsInChildren<Collider>(true).Length, Is.GreaterThan(0));
            Assert.That(ally.overhead.pips.Length, Is.GreaterThanOrEqualTo(effect.healthUpgraded), "One pip per health point.");
        }

        [Test]
        public void HollowPinesCanBeAwakened()
        {
            var pine = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Expedition/Prefabs/Pine.prefab");
            Assert.That(pine.GetComponent<AwakenableTree>(), Is.Not.Null, "The tree prefab of the hollow is marked.");
            var trunk = pine.GetComponentInChildren<Collider>();
            Assert.That(trunk, Is.Not.Null);
            Assert.That(trunk.GetComponentInParent<AwakenableTree>(true), Is.Not.Null, "A card hitting the trunk finds the tree.");
        }
    }
}
