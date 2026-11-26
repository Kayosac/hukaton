#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Trophy cards come into the hand without a limit. With twelve cards in the hand and the leftmost one selected (raised and
    /// magnified), no card is drawn over the standing controls line in the bottom-left corner («R — сбросить руку», measured
    /// by the real width of its text) nor over the card counter in the bottom-right corner; the same with «Пора домой» (alone
    /// in the hand) under the longer line, «Удерживай ЛКМ — домой». Real scene and HUD at the Game View's resolution; the save goes to a temporary folder
    /// of the test (GameSaveFolder): the player's own files are never touched.
    /// </summary>
    public sealed class HandFanClearancePlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        GameLoop game;
        ExpeditionWorld world;

        [UnitySetUp]
        public IEnumerator DepartWithFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(HandFanClearancePlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
            game = Object.FindAnyObjectByType<GameLoop>();
            world = game.expedition;
            yield return WaitFor(() => world.InPrologue && world.Running, 10);
            game.SkipTutorial();
            Assert.That(game.TryDepart(), Is.True);
            yield return WaitFor(() => world.Running && !world.InPrologue, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Hand fan clearance teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        /// <summary>Screen pixel of a world point: the view may render at its own resolution, scaled over the whole screen.</summary>
        static Vector2 Pixel(Camera eye, Vector3 world)
        {
            Vector3 viewport = eye.WorldToViewportPoint(world);
            return new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "timed out");
        }

        [UnityTest]
        public IEnumerator TwelveCardsKeepOffTheControlsLineAndTheCounter()
        {
            var trip = world.workshop.Model.Current;
            var cards = world.workshop.config.cards.Where(c => c && !c.dummy && c.purchasePrice > 0).ToArray();
            for (int i = 0; trip.Deck.Hand.Count < 12; i++) trip.GrantToHand(cards[i % cards.Length].ToSpec());
            world.thrower.SelectCopy(trip.Deck.Hand[0].Id);
            yield return Wait(1.5f);
            Check("12 cards, the leftmost selected", 12);

            // «Пора домой» (always alone in the hand) under the longer line, «Удерживай ЛКМ — домой».
            var home = trip.GrantReturn();
            Assert.That(home, Is.Not.Null);
            world.thrower.SelectCopy(home.Id);
            yield return Wait(1.5f);
            Assert.That(world.hud.controls.text, Is.EqualTo(world.hud.returnControl));
            Check("«Пора домой» selected", 1);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TenCardsKeepOffThePickupHint()
        {
            var trip = world.workshop.Model.Current;
            var cards = world.workshop.config.cards.Where(c => c && !c.dummy && c.purchasePrice > 0).ToArray();
            for (int i = 0; trip.Deck.Hand.Count < 10; i++) trip.GrantToHand(cards[i % cards.Length].ToSpec());
            world.thrower.SelectCopy(trip.Deck.Hand[0].Id);
            // A find right in front of the eye: «E — подобрать» over the controls line.
            var eye = world.viewCamera.transform;
            var item = world.lootCatalog.items.First(x => x && x.price > 0);
            var find = world.SpawnLoot(item, eye.position + eye.forward * 1.1f);
            Assert.That(find, Is.Not.Null);
            foreach (var body in find.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
            yield return WaitFor(() => world.FocusedLoot != null, 3);
            yield return Wait(1.5f);
            Assert.That(world.hud.context.text, Is.EqualTo("E — подобрать"));
            Check("10 cards, the leftmost selected, a find in focus", 10, world.hud.context);
            Check("10 cards, the leftmost selected, a find in focus", 10);
        }

        [UnityTest]
        public IEnumerator TheFirstLinesHoldFiveSecondsClearOfTheHand()
        {
            var trip = world.workshop.Model.Current;
            var cards = world.workshop.config.cards.Where(c => c && !c.dummy && c.purchasePrice > 0).ToArray();
            for (int i = 0; trip.Deck.Hand.Count < 10; i++) trip.GrantToHand(cards[i % cards.Length].ToSpec());
            world.thrower.SelectCopy(trip.Deck.Hand[0].Id);
            Assert.That(world.NoteAnthillApproached(), Is.True);
            world.Announce("Проходящее уведомление", 1);
            yield return null;
            Assert.That(world.hud.notice.text, Is.EqualTo(ExpeditionWorld.AnthillLine), "A passing notice does not replace the line.");
            Assert.That(ExpeditionWorld.FirstLineSeconds, Is.GreaterThanOrEqualTo(5));
            yield return Wait(4.6f);
            Assert.That(world.hud.notice.text, Is.EqualTo(ExpeditionWorld.AnthillLine), "It stays five seconds.");
            // Above every card of the hand, the raised one included: the fan never covers it.
            var corners = new Vector3[4];
            world.hud.notice.rectTransform.GetWorldCorners(corners);
            float noticeBottom = corners.Min(c => c.y);
            Assert.That(noticeBottom, Is.GreaterThan(Screen.height * .5f), "The notice is in the upper half.");
            foreach (var view in world.hand.GetComponentsInChildren<CardView>().Where(v => v.Visible))
                foreach (var c in new[] { new Vector3(-.5f, .5f), new Vector3(.5f, .5f) })
                    Assert.That(Pixel(world.viewCamera, view.transform.TransformPoint(c)).y, Is.LessThan(noticeBottom), "A card reaches the notice.");
            yield return Wait(.6f);
            world.Announce("Проходящее уведомление", 1);
            yield return null;
            Assert.That(world.hud.notice.text, Is.EqualTo("Проходящее уведомление"), "After its time the line gives way.");
        }

        /// <summary>Every visible card at the line's (the counter's) height stays right of its end (left of the counter).</summary>
        void Check(string what, int cardCount, TMPro.TMP_Text leftLine = null)
        {
            var hud = world.hud;
            var eye = world.viewCamera;
            // The line's real end: its text's width from the left of its rect, in screen pixels (overlay canvas).
            var text = leftLine ? leftLine : hud.controls;
            var line = text.rectTransform;
            var size = text.GetPreferredValues(text.text);
            Vector2 lineEnd = line.TransformPoint(new Vector2(line.rect.xMin + size.x, line.rect.yMin + Mathf.Max(size.y, line.rect.height)));
            var plate = (RectTransform)hud.cardCounter.transform.parent;
            var plateCorners = new Vector3[4];
            plate.GetWorldCorners(plateCorners);
            float counterLeft = plateCorners.Min(c => c.x), counterTop = plateCorners.Max(c => c.y);
            Assert.That(lineEnd.x, Is.GreaterThan(0).And.LessThan(Screen.width * .5f), "The line sits in the bottom-left corner.");

            var views = world.hand.GetComponentsInChildren<CardView>().Where(v => v.Visible).ToArray();
            Assert.That(views.Length, Is.EqualTo(cardCount), what);
            float leftmost = float.PositiveInfinity, rightmost = float.NegativeInfinity;
            foreach (var view in views)
            {
                var corners = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) }
                    .Select(c => (Vector2)Pixel(eye, view.transform.TransformPoint(c))).ToList();
                leftmost = Mathf.Min(leftmost, MinXBelow(corners, lineEnd.y));
                rightmost = Mathf.Max(rightmost, -MinXBelow(corners.Select(c => new Vector2(-c.x, c.y)).ToList(), counterTop));
            }
            Assert.That(leftmost, Is.GreaterThan(lineEnd.x), $"{what}: a card is drawn over the controls line at {Screen.width}×{Screen.height}");
            Assert.That(rightmost, Is.LessThan(counterLeft), $"{what}: a card is drawn over the card counter at {Screen.width}×{Screen.height}");
            Assert.That(leftmost, Is.GreaterThan(0), "No card leaves the screen.");
            Assert.That(rightmost, Is.LessThan(Screen.width));
        }

        /// <summary>Leftmost x of the quad below <paramref name="top"/> (screen y up); +∞ if none of it is that low.</summary>
        static float MinXBelow(List<Vector2> quad, float top)
        {
            float min = float.PositiveInfinity;
            for (int i = 0; i < quad.Count; i++)
            {
                Vector2 a = quad[i], b = quad[(i + 1) % quad.Count];
                if (a.y <= top) min = Mathf.Min(min, a.x);
                if ((a.y - top) * (b.y - top) < 0) min = Mathf.Min(min, Mathf.Lerp(a.x, b.x, (top - a.y) / (b.y - a.y)));
            }
            return min;
        }
    }
}
#endif
