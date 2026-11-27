#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using Hakaton.Backroom;
using Hakaton.Computer;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance stage 10, the tours when they are needed: none starts by itself before the first outing; the brazier's at an
    /// opening with a «Пустышка» or at the player's first burn; the PC's in parts — the desktop's short look at a start-up,
    /// «Карты» at their opening, the merge only with two equal copies, «Прокачка» at its opening; the help's replay plays
    /// them one after another. In the real scene; the save goes to a temporary folder of the test.
    /// </summary>
    public sealed class GuidePartsPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(GuidePartsPlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            var pc = Object.FindAnyObjectByType<PcComputer>();
            if (pc) pc.CloseImmediately();
            var brazier = Object.FindAnyObjectByType<Brazier>();
            if (brazier) brazier.Close();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Guide parts teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator TheToursComeWhenTheyAreNeeded()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            var pc = Object.FindAnyObjectByType<PcComputer>();
            var guide = Object.FindAnyObjectByType<PcGuide>(FindObjectsInactive.Include);
            var brazier = Object.FindAnyObjectByType<Brazier>(FindObjectsInactive.Include);
            var fire = Object.FindAnyObjectByType<BrazierGuide>(FindObjectsInactive.Include);
            Assert.That(pc && guide && brazier && fire, Is.True, "the backroom's PC and brazier with their guides");
            var model = pc.workshop.Model;
            var progress = guide.book.Progress;
            Assert.That(model.Journeys, Is.Zero);
            model.Wallet.Credit(5000); // the gas tax of the burns below
            Assert.That(model.Purchase(pc.workshop.config.dummyCard.id, 0, out _), Is.Null, "a dummy in the collection");

            // Before the first outing: nothing starts by itself.
            yield return OpenPc(pc);
            yield return Frames(10);
            Assert.That(guide.Running, Is.False, "no desktop part before the first outing");
            pc.desktop.Open(pc.cards.window);
            yield return Frames(5);
            Assert.That(guide.Running, Is.False, "no «Карты» part before the first outing");
            ClosePc(pc);
            Assert.That(brazier.Open(), Is.True);
            yield return Frames(5);
            Assert.That(fire.Running, Is.False, "no brazier tour before the first outing, even with a dummy");
            brazier.Close();
            yield return Frames(2);

            // The first outing (a defeat brings the player home).
            yield return OneOuting(game);
            Assert.That(model.Journeys, Is.EqualTo(1));

            // The brazier: no dummy — nothing at an opening; the player's own first burn — the tour.
            foreach (var dummy in model.Owned.Where(c => c.Spec.Dummy).ToList()) Assert.That(model.Burn(dummy.Id), Is.Null);
            Assert.That(model.Owned.Any(c => c.Spec.Dummy), Is.False, "the dummy is gone (it stayed home or was burned)");
            Assert.That(model.Buy("branch_bridge", out var burnable), Is.Null); // one copy only: no pair yet
            Assert.That(brazier.Open(), Is.True);
            yield return Frames(5);
            Assert.That(fire.Running, Is.False, "an opening without a dummy: no tour");
            var tile = brazier.Tiles.First(t => t && t.Slot == burnable.Id);
            typeof(Brazier).GetMethod("OnTilePressed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(brazier, new object[] { tile });
            yield return null;
            Assert.That(fire.Running, Is.True, "the first burn brings the tour");
            Assert.That(brazier.BurnLocked, Is.True, "and holds the burn while it talks");
            brazier.Close();
            yield return Frames(2);
            Assert.That(fire.Running, Is.False);
            progress.Forget(BrazierGuide.TourId);
            Assert.That(model.Purchase(pc.workshop.config.dummyCard.id, 0, out _), Is.Null);
            Assert.That(brazier.Open(), Is.True);
            yield return Frames(3);
            Assert.That(fire.Running, Is.True, "an opening with a dummy brings it");
            brazier.Close();
            yield return Frames(2);
            Assert.That(model.Burn(model.Owned.First(c => c.Spec.Dummy).Id), Is.Null);

            // The PC: the desktop's short look at the start-up.
            yield return OpenPc(pc);
            yield return WaitFor(() => guide.Running, 3);
            Assert.That(guide.Tour, Is.EqualTo(guide.desktopTour.id));
            int steps = 0;
            yield return ReadThrough(guide, guide.desktopTour.id, n => steps = n);
            Assert.That(steps, Is.InRange(1, 3), "three steps at most");
            Assert.That(guide.Seen(guide.desktopTour.id), Is.True);
            Assert.That(pc.cards.window.IsOpen, Is.False, "it does not open «Карты» itself");

            // «Карты» without two equal copies: their own part only.
            Assert.That(guide.MergeDue, Is.False);
            pc.desktop.Open(pc.cards.window);
            yield return Frames(2);
            Assert.That(guide.Tour, Is.EqualTo(guide.cardsTour.id));
            ClosePc(pc);
            yield return Frames(2);
            progress.Finish(guide.cardsTour.id);
            yield return OpenPc(pc);
            pc.desktop.Open(pc.cards.window);
            yield return Frames(5);
            Assert.That(guide.Running, Is.False, "«Карты» seen, no pair: nothing");
            Assert.That(guide.Seen(guide.mergeTour.id), Is.False);
            ClosePc(pc);

            // Two equal copies: the merge at the next opening of «Карты».
            Assert.That(model.Buy("strike", out _), Is.Null);
            Assert.That(guide.MergeDue, Is.True);
            yield return OpenPc(pc);
            Assert.That(guide.Running, Is.False);
            pc.desktop.Open(pc.cards.window);
            yield return Frames(2);
            Assert.That(guide.Tour, Is.EqualTo(guide.mergeTour.id), "the merge, shown on two pretend cards");
            ClosePc(pc);
            yield return Frames(2);

            // «Прокачка» at its opening.
            yield return OpenPc(pc);
            pc.desktop.Open(pc.upgrades.window);
            yield return Frames(2);
            Assert.That(guide.Tour, Is.EqualTo(guide.upgradesTour.id));
            ClosePc(pc);
            yield return Frames(2);

            // The replay: all parts, one after another, each window opened for its part.
            yield return OpenPc(pc);
            guide.Replay();
            Assert.That(guide.Tour, Is.EqualTo(guide.desktopTour.id));
            yield return ReadThrough(guide, guide.desktopTour.id, _ => { });
            yield return Frames(2);
            Assert.That(guide.Tour, Is.EqualTo(guide.cardsTour.id), "«Карты» next");
            Assert.That(pc.cards.window.IsOpen, Is.True, "opened for it");
            ClosePc(pc);
        }

        // Windows left open would greet the next start-up (a short wake carries on with them): shut them first.
        static void ClosePc(PcComputer pc)
        {
            pc.desktop.CloseAll();
            pc.CloseImmediately();
        }

        static IEnumerator OpenPc(PcComputer pc)
        {
            Assert.That(pc.Open(), Is.True);
            yield return WaitFor(() => pc.Running, 8);
            Assert.That(pc.Running, Is.True, "the PC boots up");
            yield return null;
        }

        /// <summary>Reads a part to its end (E on every step) and counts the steps it showed.</summary>
        static IEnumerator ReadThrough(PcGuide guide, string tour, System.Action<int> counted)
        {
            int steps = 0;
            object last = null;
            float end = Time.realtimeSinceStartup + 30;
            while (guide.Tour == tour && Time.realtimeSinceStartup < end)
            {
                if (guide.Step != last) { last = guide.Step; steps++; }
                guide.Continue();
                yield return null;
            }
            Assert.That(guide.Tour, Is.Not.EqualTo(tour), tour + " read to its end");
            counted(steps);
        }

        static IEnumerator OneOuting(GameLoop game)
        {
            var world = game.expedition;
            Assert.That(game.TryDepart(), Is.True, "the shop can leave on an outing");
            yield return WaitFor(() => world.Running, 15);
            Assert.That(world.Running, Is.True);
            world.vitals.Immortal = false;
            world.vitals.Hurt(world.vitals.HalfHearts, Vector3.zero, null, true);
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 15);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
            yield return Frames(5);
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
