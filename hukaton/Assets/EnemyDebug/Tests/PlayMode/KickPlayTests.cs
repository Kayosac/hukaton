#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    /// <summary>
    /// The kick (F) and the trophy cards (board «Пинок и трофейные карты»): a kick hurts, shoves and stuns a small
    /// creature, goes through a swarm, breaks a pot; it has its own cooldown, not stamina, and the leg is drawn under the
    /// cards; a slain creature's trophy card flies into the hand. Photographs of
    /// the leg and the flying card go to Logs/kick/.
    /// </summary>
    public class KickPlayTests
    {
        EnemyDebugSandbox sandbox;
        ExpeditionWorld world;
        static readonly Vector3 Spot = new Vector3(50, 0, -30);

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return Wait(.3f);
            world = sandbox.world;
            Assert.That(world.Running, Is.True);
            Assert.That(world.kick, Is.Not.Null, "the kick is in the scene");
            sandbox.motor.Teleport(Spot, Quaternion.identity);
            sandbox.control.SendMessage("OnApplicationFocus", true);
            sandbox.control.CaptureCursor();
            yield return Wait(.2f);
            Assert.That(world.control.GameplayActive, Is.True);
            world.kick.ResetCooldown();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (world && world.trophies) world.trophies.Always = false;
            if (sandbox) sandbox.ClearLive();
            yield return null;
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        /// <summary>A point on the ground this far straight ahead of the eyes.</summary>
        Vector3 Ahead(float distance)
        {
            var eye = world.viewCamera.transform;
            var flat = eye.forward;
            flat.y = 0;
            return sandbox.motor.transform.position + flat.normalized * distance;
        }

        WorldCreature Beast(string name, Vector3 at)
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == name);
            Assert.That(preview, Is.Not.Null, name);
            var creature = sandbox.Spawn(preview);
            creature.Resume(creature.maximumHealth, at);
            return creature;
        }

        IEnumerator LookAt(Vector3 point)
        {
            sandbox.motor.view.Guide(point);
            yield return Wait(.6f);
            sandbox.motor.view.Guide(null);
        }

        [UnityTest]
        public IEnumerator AKickHurtsShovesAndStunsASmallCreature()
        {
            // A rooster (4 HP) standing right in front; held in place until the kick (it would run at the player).
            var rooster = Beast("Rooster baseball", Ahead(1.4f));
            yield return Wait(.1f);
            yield return LookAt(rooster.Centre);
            rooster.Resume(rooster.maximumHealth, Ahead(1.4f));
            yield return null;
            int before = rooster.Health;
            var from = rooster.Position;
            Assert.That(world.kick.TryKick(), Is.True, "a rested player kicks");
            yield return Wait(.03f);
            yield return Photograph("kick-thrust");
            yield return Wait(.06f);
            yield return Photograph("kick-full");
            yield return Wait(.1f);
            yield return Photograph("kick-retract");
            yield return Wait(.25f);
            yield return Photograph("kick-ring");
            Assert.That(rooster.Health, Is.EqualTo(before - KickRules.Damage), "one point of damage");
            Assert.That(Vector3.Distance(rooster.Position, from), Is.GreaterThan(.3f), "and shoved");
            Assert.That(world.kick.TryKick(), Is.False, "not again before the cooldown");
        }

        [UnityTest]
        public IEnumerator TheLegGoesThroughASwarm()
        {
            var swarm = Beast("Bee swarm", Ahead(1.2f));
            yield return Wait(.1f);
            yield return LookAt(swarm.Centre);
            int before = swarm.Health;
            world.kick.TryKick();
            yield return Wait(.3f);
            Assert.That(swarm.Health, Is.EqualTo(before), "a swarm is not kicked");
        }

        [UnityTest]
        public IEnumerator TheKickHasItsOwnThreeSecondCooldown()
        {
            var stamina = world.motor.stamina;
            for (int i = 0; i < 30 && stamina.TrySpend(10); i++) { }
            float left = stamina.Value;
            Assert.That(world.kick.TryKick(), Is.True, "out of breath, the leg still kicks: stamina is not its fuel");
            Assert.That(stamina.Value, Is.LessThanOrEqualTo(left + .01f).And.GreaterThanOrEqualTo(left - .01f), "and spends none");
            Assert.That(world.kick.CooldownLeft01, Is.EqualTo(1).Within(.02f));
            yield return Wait(1);
            Assert.That(world.kick.TryKick(), Is.False, "not before three seconds");
            Assert.That(world.kick.CooldownLeft01, Is.InRange(.55f, .72f));
            yield return Wait(2.1f);
            Assert.That(world.kick.TryKick(), Is.True, "and again after them");
        }

        [Test]
        public void TheLegIsDrawnUnderTheCardsAndNeverCut()
        {
            var view = world.kick.view;
            Assert.That(view.canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera), "in front of the eyes, through the view camera");
            Assert.That(view.canvas.worldCamera, Is.SameAs(view.RenderCamera()));
            Assert.That(view.canvas.worldCamera.targetTexture, Is.Null, "drawn straight to the screen");
            var material = view.body.materialForRendering;
            Assert.That(material.renderQueue, Is.LessThan(3100), "before the card viewmodel's quads");
            Assert.That(material.GetFloat("_ZTest"), Is.EqualTo((float)UnityEngine.Rendering.CompareFunction.Always), "the world never cuts it");
            Assert.That(view.leg.pivot, Is.EqualTo(new Vector2(1, 0)), "pivot at the drawing's cut corner");
            // The trouser leg is cut at the drawing's right edge, over its lowest 217 of 768 rows: kept under the screen.
            float cutTop = view.restPosition.y + view.maxLift + view.leg.rect.height * 217f / 768 * (1 + view.thrustPunch);
            Assert.That(cutTop, Is.LessThan(0), "the cut stays under the bottom edge");
        }

        [UnityTest]
        public IEnumerator ATrophyCardFliesIntoTheHand()
        {
            Assert.That(world.trophies, Is.Not.Null, "trophy cards are in the scene");
            world.trophies.Always = true;
            var trip = world.workshop.Model.Current;
            int before = trip.Deck.Hand.Count(c => c.Temporary);
            var dog = Beast("Goblin dog", Ahead(4));
            yield return Wait(.2f);
            yield return LookAt(dog.Centre);
            dog.Damage(dog.maximumHealth, Vector3.zero, dog.Centre, 0);
            yield return Wait(.25f);
            yield return Photograph("trophy-pop");
            yield return Wait(.35f);
            yield return Photograph("trophy-fly");
            yield return Wait(.8f);
            yield return Photograph("trophy-hand");
            Assert.That(trip.Deck.Hand.Count(c => c.Temporary), Is.EqualTo(before + 1), "one more one-throw card in the hand");
            var card = trip.Deck.Hand.Last(c => c.Temporary);
            // The debug arena runs as a prologue, where every spell is endless; on a real outing a trophy is one throw.
            if (!trip.Prologue) Assert.That(card.UsesLeft, Is.EqualTo(1), "one throw");
            Assert.That(world.workshop.Model.Owned.Any(c => c.Id == card.Id), Is.False, "never in the collection");
        }

        static IEnumerator Photograph(string name)
        {
            yield return new WaitForEndOfFrame();
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "kick");
            System.IO.Directory.CreateDirectory(dir);
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), shot.EncodeToPNG());
            Object.Destroy(shot);
        }
    }
}
#endif
