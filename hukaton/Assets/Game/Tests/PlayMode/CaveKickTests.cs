#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Tutorial;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The cave teaches the kick (board «Пинок и трофейные карты»): four instruction cards — throw, move, kick, discard;
    /// the kick's card leaves the hand once the player kicks after the first two throws, and only then does R work.
    /// The player's own save files are moved aside for the run and put back afterwards.
    /// </summary>
    public sealed class CaveKickTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            saveFolder = Path.Combine(Application.persistentDataPath, "Hakaton");
            playerSaves.Clear();
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder))
                {
                    playerSaves[file] = File.ReadAllBytes(file);
                    File.Delete(file);
                }
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Cave kick teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "timed out");
        }

        [UnityTest]
        public IEnumerator TheCaveTeachesTheKickBetweenMoveAndDiscard()
        {
            var cave = Object.FindAnyObjectByType<CaveTutorial>();
            var world = Object.FindAnyObjectByType<GameLoop>().expedition;
            Assert.That(world.kick, Is.Not.Null, "the kick is in the game");
            Assert.That(cave.kickInstruction, Is.Not.Null);
            yield return WaitFor(() => cave.Stage == CaveStage.Instructions, 10);
            var trip = world.workshop.Model.Current;
            var hand = trip.Deck.Hand;
            Assert.That(hand.Select(c => c.Spec.Id).ToArray(),
                Is.EqualTo(new[] { cave.throwInstruction.id, cave.moveInstruction.id, cave.kickInstruction.id, cave.discardInstruction.id }));
            var kickCard = hand[2];
            Assert.That(cave.CanThrow(kickCard), Is.False, "the kick's card is not thrown: F uses it");
            Assert.That(cave.CanThrow(hand[3]), Is.False, "the discard's card is used by R alone");

            world.control.SendMessage("OnApplicationFocus", true);
            world.control.CaptureCursor();
            yield return null;

            // A kick before the two throws changes nothing.
            Assert.That(world.kick.TryKick(), Is.True);
            yield return new WaitForSeconds(1);
            Assert.That(trip.Deck.Hand.Contains(kickCard), Is.True, "too early");

            // The two throws (as the thrower reports them), then the kick.
            foreach (var card in hand.Take(2).ToList())
            {
                trip.Release(card.Id);
                var thrown = typeof(CaveTutorial).GetMethod("OnThrown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                thrown.Invoke(cave, new object[] { card });
            }
            Assert.That(cave.CanDiscard(), Is.False, "R waits for the kick");
            world.kick.ResetCooldown();
            Assert.That(world.kick.TryKick(), Is.True);
            yield return null;
            Assert.That(trip.Deck.Hand.Contains(kickCard), Is.False, "the kick's card has done its job");
            Assert.That(cave.CanDiscard(), Is.True, "now R");
            Assert.That(world.thrower.Selected?.Spec.Id, Is.EqualTo(cave.discardInstruction.id), "the discard's card is up next");
        }
        /// <summary>The leg is drawn in the real game too, where the world goes through a low-resolution image (Logs/kick/game-kick.png).</summary>
        [UnityTest]
        public IEnumerator TheLegShowsInTheGame()
        {
            var world = Object.FindAnyObjectByType<GameLoop>().expedition;
            var cave = Object.FindAnyObjectByType<CaveTutorial>();
            yield return WaitFor(() => cave.Stage == CaveStage.Instructions, 10);
            world.control.SendMessage("OnApplicationFocus", true);
            world.control.CaptureCursor();
            yield return null;
            yield return new WaitForSeconds(2.5f);
            var view = world.kick.view;
            var log = new System.Text.StringBuilder();
            log.AppendLine("viewCamera=" + (world.viewCamera ? world.viewCamera.name + " enabled=" + world.viewCamera.isActiveAndEnabled + " near=" + world.viewCamera.nearClipPlane + " mask=" + world.viewCamera.cullingMask + " rt=" + world.viewCamera.targetTexture : "null"));
            foreach (var cam in Camera.allCameras) log.AppendLine("camera " + cam.name + " depth=" + cam.depth + " near=" + cam.nearClipPlane + " mask=" + cam.cullingMask + " rt=" + cam.targetTexture);
            world.kick.ResetCooldown();
            Assert.That(world.kick.TryKick(), Is.True);
            yield return new WaitForSeconds(.08f);
            log.AppendLine("canvas mode=" + view.canvas.renderMode + " cam=" + (view.canvas.worldCamera ? view.canvas.worldCamera.name : "null") + " enabled=" + view.canvas.enabled + " active=" + view.canvas.isActiveAndEnabled + " plane=" + view.canvas.planeDistance + " layer=" + view.canvas.gameObject.layer);
            Assert.That(view.canvas.worldCamera, Is.Not.Null.And.SameAs(view.RenderCamera()));
            Assert.That(view.canvas.worldCamera.targetTexture, Is.Null, "the leg's camera draws to the screen, not into the low-resolution 3D image (whose camera draws no transparents)");
            Assert.That(view.canvas.enabled && view.Showing, Is.True);
            log.AppendLine("leg pos=" + view.leg.anchoredPosition + " scale=" + view.leg.localScale + " sprite=" + (view.body.sprite ? view.body.sprite.name : "null") + " colour=" + view.body.color + " mat=" + view.body.materialForRendering.name + " ppu=" + view.body.materialForRendering.GetFloat("_PixelsPerUnit") + " showing=" + view.Showing);
            foreach (var card in Object.FindObjectsByType<Hakaton.Expeditions.CardView>(FindObjectsSortMode.None))
                if (card.Visible)
                {
                    Assert.That(card.Front, Is.Not.Null, card.name + ": every card in the hand has its drawing (the kick's note is a story card)");
                    log.AppendLine("card " + card.name + " front=" + (card.Front ? card.Front.name + " " + card.Front.width + "x" + card.Front.height + " " + card.Front.GetType().Name : "null") + " order=" + card.meshRenderer.sortingOrder);
                }
            foreach (var c in world.workshop.Model.Current.Deck.Hand) log.AppendLine("hand " + c.Spec.Id);
            var corners = new Vector3[4];
            view.leg.GetWorldCorners(corners);
            foreach (var c in corners) log.AppendLine("corner viewport " + view.canvas.worldCamera?.WorldToViewportPoint(c));
            yield return new WaitForEndOfFrame();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "kick");
            Directory.CreateDirectory(dir);
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, "game-kick.png"), shot.EncodeToPNG());
            Object.Destroy(shot);
            File.WriteAllText(Path.Combine(dir, "game-kick.txt"), log.ToString());
        }
    }
}
#endif
