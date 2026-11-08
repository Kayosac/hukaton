using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Character.Tests
{
    public sealed class SpriteNpcActorTests
    {
        GameObject fixture;
        NpcAppearance appearance;
        SpriteNpcActor actor;

        [SetUp]
        public void CreateIsolatedRouteAndRigUsingImportedAtlas()
        {
            fixture = new GameObject("NPC test fixture");
            var player = new GameObject("Viewer");
            player.transform.SetParent(fixture.transform);
            player.transform.position = new Vector3(0, 1.6f, -5);
            appearance = ScriptableObject.CreateInstance<NpcAppearance>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/Content/NPCs/Peasant/PeasantAtlas.png").OfType<Sprite>().ToArray();
            appearance.neutral = sprites.Single(s => s.name == "Peasant_DefaultFront");
            appearance.back = sprites.Single(s => s.name == "Peasant_Back");
            appearance.displeased = sprites.Single(s => s.name == "Peasant_DispleasedFront");
            appearance.happy = sprites.Single(s => s.name == "Peasant_HappyFront");
            appearance.angry = sprites.Single(s => s.name == "Peasant_EnragedFront");
            appearance.bodyScale = .18f;

            var root = new GameObject("NPC");
            root.SetActive(false);
            root.transform.SetParent(fixture.transform);
            var facing = new GameObject("Facing").transform;
            facing.SetParent(root.transform, false);
            var step = new GameObject("Feet pivot").transform;
            step.SetParent(facing, false);
            step.localScale = Vector3.one * .18f;
            var renderer = step.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = appearance.neutral;
            actor = root.AddComponent<SpriteNpcActor>();
            actor.spriteRenderer = renderer;
            actor.facingRoot = facing;
            actor.stepRoot = step;
            actor.viewer = player.transform;
            actor.defaultAppearance = appearance;
            actor.route = new Transform[3];
            var positions = new[] { Vector3.zero, new Vector3(0, 0, 2), new Vector3(2, 0, 2) };
            for (int i = 0; i < positions.Length; i++)
            {
                actor.route[i] = new GameObject("Waypoint " + i).transform;
                actor.route[i].SetParent(fixture.transform);
                actor.route[i].position = positions[i];
            }
            root.SetActive(true);
        }

        [TearDown]
        public void DestroyOnlyFixtureObjects()
        {
            Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(appearance);
        }

        [Test]
        public void VisitShowsOutcomeThenTurnsToBackAndWalksOutBeforeHiding()
        {
            Assert.That(actor.BeginVisit(), Is.True);
            Assert.That(actor.stepRoot.localScale.x, Is.EqualTo(.18f).Within(.0001f),
                "The default appearance must preserve the size authored in Edit mode.");
            AssertSpriteUv(appearance.neutral);
            actor.Advance(2);
            Assert.That(actor.ReadyForTrade, Is.True);
            Assert.That(actor.transform.position, Is.EqualTo(actor.route[2].position));
            Vector3 counter = actor.transform.position;
            Assert.That(actor.Leave(NpcExpression.Happy), Is.True);
            Assert.That(actor.IsDeparting, Is.True);
            actor.Advance(actor.reactionDuration * .8f);
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Reacting));
            Assert.That(actor.spriteRenderer.sprite, Is.SameAs(appearance.happy));
            Assert.That(actor.transform.position, Is.EqualTo(counter));

            actor.Advance(actor.reactionDuration * .2f + actor.turnDuration * .25f);
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Turning));
            Assert.That(actor.spriteRenderer.sprite, Is.SameAs(appearance.happy));
            actor.Advance(actor.turnDuration * .5f);
            Assert.That(actor.spriteRenderer.sprite, Is.SameAs(appearance.back));
            AssertSpriteUv(appearance.back);
            actor.Advance(actor.turnDuration * .25f + .05f);
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Leaving));
            Assert.That(actor.spriteRenderer.enabled, Is.True);
            Assert.That(Vector3.Distance(actor.transform.position, counter), Is.GreaterThan(.05f));
            actor.Advance(5);
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Idle));
            Assert.That(actor.spriteRenderer.enabled, Is.False);
            Assert.That(actor.transform.position, Is.EqualTo(actor.route[0].position));
        }

        [Test]
        public void CancellationMidApproachRetracesTheReachedCornerWithoutTeleporting()
        {
            actor.BeginVisit();
            actor.Advance(1.2f);
            Vector3 stopped = actor.transform.position;
            Assert.That(stopped.x, Is.GreaterThan(.5f));
            Assert.That(stopped.z, Is.EqualTo(2).Within(.0001f));
            Assert.That(actor.BeginVisit(), Is.False);
            actor.Cancel();
            Assert.That(actor.transform.position, Is.EqualTo(stopped));
            Assert.That(actor.spriteRenderer.enabled, Is.True);
            actor.Advance(actor.turnDuration + .2f);
            Assert.That(actor.transform.position.x, Is.LessThan(stopped.x));
            Assert.That(actor.transform.position.x, Is.GreaterThan(0));
            Assert.That(actor.transform.position.z, Is.EqualTo(2).Within(.0001f),
                "Return to the reached corner before heading to the exit, without cutting diagonally through the scene.");
            actor.Advance(.2f);
            Assert.That(actor.transform.position.x, Is.EqualTo(0).Within(.0001f));
            Assert.That(actor.transform.position.z, Is.LessThan(2));
            actor.Advance(5);
            Assert.That(actor.IsBusy, Is.False);
            Assert.That(actor.transform.position, Is.EqualTo(actor.route[0].position));
        }

        [Test]
        public void RouteSpeedSurvivesWaypointCrossingsAndSteppingKeepsFeetFixed()
        {
            actor.BeginVisit();
            actor.Advance(1.4f);
            Vector3 singleStepPosition = actor.transform.position;
            Assert.That(singleStepPosition.x, Is.EqualTo(1.4f * actor.walkSpeed - 2).Within(.0001f));
            Assert.That(actor.spriteRenderer.bounds.min.y, Is.EqualTo(0).Within(.0001f));
            Assert.That(actor.stepRoot.localScale.y, Is.InRange(.18f * .96f, .18f * 1.04f));
            actor.Cancel();
            actor.Advance(10);
            actor.BeginVisit();
            for (int i = 0; i < 14; i++) actor.Advance(.1f);
            Assert.That(Vector3.Distance(actor.transform.position, singleStepPosition), Is.LessThan(.0001f));
            actor.Advance(1);
            actor.Advance(2);
            Assert.That(actor.ReadyForTrade, Is.True);
            Assert.That(actor.stepRoot.localScale.y, Is.EqualTo(.18f).Within(.0001f),
                "Stepping must settle completely when the NPC is stationary.");
            Assert.That(actor.spriteRenderer.bounds.min.y, Is.EqualTo(0).Within(.0001f));
        }

        void AssertSpriteUv(Sprite expected)
        {
            var block = new MaterialPropertyBlock();
            actor.spriteRenderer.GetPropertyBlock(block);
            Vector4 actual = block.GetVector("_SpriteUVRect");
            Rect rect = expected.textureRect;
            var uv = new Vector4(rect.x / expected.texture.width, rect.y / expected.texture.height,
                rect.width / expected.texture.width, rect.height / expected.texture.height);
            Assert.That(Vector4.Distance(actual, uv), Is.LessThan(.000001f));
        }
    }
}
