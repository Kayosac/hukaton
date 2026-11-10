using System;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// The awakened tree looks like the owner's «Зловещее ожившее дерево»: its atlas drawings, its 3 m size, and which
    /// drawing each moment of the ally shows (the aggression drawing with its red «!» only for the wind-up).
    /// </summary>
    public sealed class TreeAllyLookTests
    {
        const string AllyPath = "Assets/Game/Prefabs/Cards/TreeAwakening/Awakened tree.prefab";
        const string EnemyPath = "Assets/Game/Prefabs/Sinister living tree.prefab";
        const float Hold = .2f;

        static TreeAlly Ally => AssetDatabase.LoadAssetAtPath<GameObject>(AllyPath).GetComponent<TreeAlly>();

        // ---------- which drawing ----------

        [Test]
        public void TheAggressionDrawingShowsOnlyDuringTheWindup()
        {
            foreach (TreeAllyPose pose in Enum.GetValues(typeof(TreeAllyPose)))
                foreach (float hurt in new[] { 0f, 1f })
                    foreach (float since in new[] { 0f, .1f, 5f })
                    {
                        var drawing = SpriteTreeAllyLook.DrawingFor(pose, hurt, since, Hold);
                        Assert.That(drawing == CreaturePose.Alarm, Is.EqualTo(pose == TreeAllyPose.Windup),
                            $"{pose}, hurt {hurt}, {since} s after the strike");
                    }
        }

        [Test]
        public void EachMomentOfTheTreeHasItsDrawing()
        {
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Idle, 0, 9, Hold), Is.EqualTo(CreaturePose.Default));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Walk, 0, 9, Hold), Is.EqualTo(CreaturePose.Default));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Awakening, 0, 9, Hold), Is.EqualTo(CreaturePose.Default));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Windup, 1, 9, Hold), Is.EqualTo(CreaturePose.Alarm),
                "A hit does not hide the wind-up cue.");
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Strike, 1, 0, Hold), Is.EqualTo(CreaturePose.Attack), "The swing.");
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Recover, 0, .1f, Hold), Is.EqualTo(CreaturePose.Attack),
                "The swing is held a moment after the strike window.");
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Recover, 0, .3f, Hold), Is.EqualTo(CreaturePose.Default));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Recover, 1, .3f, Hold), Is.EqualTo(CreaturePose.Hurt));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Idle, .5f, 9, Hold), Is.EqualTo(CreaturePose.Hurt));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Walk, .5f, 9, Hold), Is.EqualTo(CreaturePose.Hurt));
            Assert.That(SpriteTreeAllyLook.DrawingFor(TreeAllyPose.Dying, 1, 9, Hold), Is.EqualTo(CreaturePose.Death));
        }

        // ---------- assets ----------

        [Test]
        public void TheAllyWearsTheSinisterTreeDrawings()
        {
            var ally = Ally;
            var look = ally.look as SpriteTreeAllyLook;
            Assert.That(look, Is.Not.Null, "The look is drawn from the Sinister living tree atlas.");
            Assert.That(PrefabUtility.IsAnyPrefabInstanceRoot(look.gameObject), Is.True, "Still a swappable child prefab.");
            var view = look.view;
            Assert.That(view, Is.Not.Null);
            Assert.That(view.spriteRenderer, Is.Not.Null);
            Assert.That(view.transform.IsChildOf(look.transform), Is.True);
            var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath).GetComponentInChildren<CreatureSpriteView>(true);
            Assert.That(view.idle, Is.SameAs(enemy.idle));
            Assert.That(view.idleBack, Is.SameAs(enemy.idleBack));
            Assert.That(view.alarm, Is.SameAs(enemy.alarm));
            Assert.That(view.hurt, Is.SameAs(enemy.hurt));
            Assert.That(view.attack, Is.SameAs(enemy.attack));
            Assert.That(view.death, Is.SameAs(enemy.death));
            Assert.That(view.deathBack, Is.SameAs(enemy.deathBack));
            Assert.That(AssetDatabase.GetAssetPath(view.idle), Does.EndWith("SinisterLivingTree/SinisterLivingTreeAtlas.png"));
            Assert.That(view.spriteRenderer.sharedMaterial, Is.SameAs(enemy.spriteRenderer.sharedMaterial), "The hand-drawn sprite shader.");
            Assert.That(view.idle.pixelsPerUnit, Is.EqualTo(128).Within(.01f), "128 texels per world metre at size 1.");
            Assert.That(view.height, Is.EqualTo(3).Within(1e-4f), "Manifest: 3 m.");
            Assert.That(look.Height, Is.EqualTo(3).Within(1e-4f));
        }

        [Test]
        public void TheAllysBodyAndMarksFitTheThreeMetreTree()
        {
            var ally = Ally;
            var capsule = ally.GetComponent<CapsuleCollider>();
            Assert.That(capsule, Is.Not.Null);
            Assert.That(capsule.center.y - capsule.height / 2, Is.InRange(-.01f, .05f), "Stands on its roots.");
            Assert.That(capsule.center.y + capsule.height / 2, Is.InRange(2.6f, 3f), "Up into the crown, not above it.");
            var agent = ally.navigator;
            Assert.That(agent.height, Is.InRange(2.6f, 3f));
            Assert.That(agent.radius, Is.EqualTo(capsule.radius).Within(1e-4f));
            Assert.That(capsule.radius, Is.LessThanOrEqualTo(NavMesh.GetSettingsByID(agent.agentTypeID).agentRadius + 1e-3f),
                "The body fits the walkable corridors baked for the agent.");
            float lowestPip = float.MaxValue;
            foreach (var pip in ally.overhead.pips) lowestPip = Mathf.Min(lowestPip, pip.transform.localPosition.y);
            Assert.That(ally.overhead.transform.localPosition.y + lowestPip, Is.GreaterThan(3.1f), "Health pips sit above the crown.");
            // The board's fight numbers did not move with the look.
            Assert.That(ally.maximumHealth, Is.EqualTo(12));
            Assert.That(ally.strikeDamage, Is.EqualTo(2));
            Assert.That(ally.reach, Is.EqualTo(2.5f));
            Assert.That(ally.attackCycle, Is.EqualTo(2.5f));
            Assert.That(ally.windup, Is.EqualTo(.8f));
            Assert.That(ally.body.mass, Is.EqualTo(3));
        }
    }
}
