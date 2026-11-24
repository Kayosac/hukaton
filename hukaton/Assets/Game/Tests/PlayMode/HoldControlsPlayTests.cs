#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// On an outing R discards the hand only after a hold (<see cref="ExpeditionSettings.discardHold"/>) and «Пора домой»
    /// takes the player home only after LMB is held (<see cref="ExpeditionSettings.returnHold"/>) from a press made once the
    /// card is selected; the HUD shows the hold's ring and the standing controls line. Real scene, real input devices fed
    /// state every frame. The save goes to a temporary folder of the test (GameSaveFolder): the player's own files are never touched.
    /// </summary>
    public sealed class HoldControlsPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        GameLoop game;
        ExpeditionWorld world;
        Keyboard keyboard, previousKeyboard;
        Mouse mouse, previousMouse;
        KeyboardState keys;
        MouseState buttons;
        InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        InputSettings.BackgroundBehavior previousBackground;
        InputSettings.UpdateMode previousUpdate;

        [UnitySetUp]
        public IEnumerator DepartWithFreshSaveAndOwnDevices()
        {
            // The save lives in a temporary folder of this test: the player's own files are never read or touched.
            GameSaveFolder.UseTemporaryForTests(nameof(HoldControlsPlayTests));
            previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousUpdate = InputSystem.settings.updateMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            keys = new KeyboardState(); buttons = new MouseState();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.onBeforeUpdate += Feed; InputSystem.onAfterUpdate += SelectDevices;

            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
            game = Object.FindAnyObjectByType<GameLoop>();
            world = game.expedition;
            Assert.That(game.InCave, Is.True, "A fresh game opens in the cave.");
            yield return WaitFor(() => world.InPrologue && world.Running, 10);
            yield return null;
            Assert.That(world.hud.controls.text, Is.Empty, "No controls line in the cave prologue.");
            game.SkipTutorial();
            // Hand and input are what is checked here, not the rendered world.
            world.viewCamera.enabled = false;
            Assert.That(game.TryDepart(), Is.True);
            yield return WaitFor(() => world.Running && !world.InPrologue, 5);
            world.control.SendMessage("OnApplicationFocus", true);
            world.control.CaptureCursor();
            yield return Wait(.2f);
            Assert.That(world.control.GameplayActive, Is.True);
        }

        [UnityTearDown]
        public IEnumerator RestoreDevicesAndThePlayersSave()
        {
            keys = new KeyboardState(); buttons = new MouseState();
            InputSystem.onBeforeUpdate -= Feed; InputSystem.onAfterUpdate -= SelectDevices;
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.updateMode = previousUpdate;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Hold controls teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        void Feed()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            InputSystem.QueueStateEvent(keyboard, keys);
            InputSystem.QueueStateEvent(mouse, buttons);
        }

        void SelectDevices() { if (InputState.currentUpdateType == InputUpdateType.Dynamic) { keyboard.MakeCurrent(); mouse.MakeCurrent(); } }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(condition(), Is.True, "timed out");
        }

        [UnityTest]
        public IEnumerator RDiscardsTheHandOnlyAfterAHold()
        {
            var trip = world.workshop.Model.Current;
            var thrower = world.thrower;
            var hud = world.hud;
            Assert.That(hud.controls.text, Is.EqualTo(hud.discardControl), "The controls line is up from the start of the outing.");
            Assert.That(hud.discardControl, Is.EqualTo("R — сбросить руку"));
            Assert.That(trip.PlayedCount, Is.Zero, "…before any throw.");
            Assert.That(trip.Deck.Hand.Count, Is.GreaterThan(0));
            int discarded = trip.DiscardedCount;
            var hand = trip.Deck.Hand.Select(c => c.Id).ToArray();

            // A short press: nothing.
            keys = new KeyboardState(Key.R);
            yield return Wait(world.settings.discardHold * .5f);
            Assert.That(thrower.DiscardHold.Active, Is.True);
            Assert.That(ExpeditionHud.HoldProgress(thrower), Is.InRange(.2f, .8f));
            Assert.That(hud.holdRing.enabled, Is.True, "The ring fills while R is held.");
            Assert.That(hud.holdRing.fillAmount, Is.InRange(.2f, .8f));
            keys = new KeyboardState();
            yield return null; yield return null;
            Assert.That(trip.DiscardedCount, Is.EqualTo(discarded), "Let go early: the hand stays.");
            Assert.That(trip.Deck.Hand.Select(c => c.Id), Is.EqualTo(hand));
            Assert.That(hud.holdRing.enabled, Is.False, "…and the ring is gone.");

            // Held long enough: the hand goes, once, however long R stays down.
            keys = new KeyboardState(Key.R);
            yield return Wait(world.settings.discardHold + .15f);
            Assert.That(trip.DiscardedCount, Is.GreaterThan(discarded), "A hold of R discards the hand.");
            int afterFirst = trip.DiscardedCount;
            Assert.That(hud.holdRing.enabled, Is.False);
            yield return Wait(world.settings.discardHold + world.settings.discardCooldown + .3f);
            Assert.That(trip.DiscardedCount, Is.EqualTo(afterFirst), "Still held: the next discard needs a new press.");
            keys = new KeyboardState();
            // A fresh game's deck is just the hand: after the discard only «Пора домой» may be left. Give a card to discard.
            if (!trip.Deck.Hand.Any(c => !c.IsReturn))
                trip.GrantToHand(world.workshop.config.cards.First(c => c && c.id == "fire").ToSpec());
            yield return null; yield return null;
            keys = new KeyboardState(Key.R);
            yield return Wait(world.settings.discardHold + .15f);
            keys = new KeyboardState();
            Assert.That(trip.DiscardedCount, Is.GreaterThan(afterFirst), "A new press and hold discards again.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReturnCardNeedsAFreshHoldOfTheLeftButton()
        {
            var trip = world.workshop.Model.Current;
            var thrower = world.thrower;
            var hud = world.hud;

            // LMB goes down on an ordinary card (a charge) and stays down while «Пора домой» comes into the hand.
            buttons = new MouseState { buttons = 1 };
            yield return Wait(.1f);
            Assert.That(thrower.Preparation.Active, Is.True);
            var card = trip.GrantReturn();
            thrower.SelectCopy(card.Id);
            yield return null;
            Assert.That(thrower.ReturnReady, Is.True);
            Assert.That(hud.controls.text, Is.EqualTo(hud.returnControl), "The controls line tells to hold LMB.");
            Assert.That(hud.returnControl, Is.EqualTo("Удерживай ЛКМ — домой"));
            yield return Wait(world.settings.returnHold + .5f);
            Assert.That(world.Returning, Is.False, "A button held from before the card was selected never counts.");
            Assert.That(thrower.ReturnHold.Active, Is.False);
            Assert.That(thrower.Preparation.Active, Is.False, "The charge on the card that left is dropped.");
            buttons = new MouseState();
            yield return null; yield return null;

            // A click: nothing.
            buttons = new MouseState { buttons = 1 };
            yield return Wait(world.settings.returnHold * .4f);
            Assert.That(hud.holdRing.enabled, Is.True, "The ring fills while LMB is held on «Пора домой».");
            Assert.That(hud.holdRing.fillAmount, Is.InRange(.1f, .8f));
            buttons = new MouseState();
            yield return null; yield return null;
            Assert.That(world.Returning, Is.False, "Let go early: still on the outing.");
            Assert.That(hud.holdRing.enabled, Is.False);

            // A fresh press held long enough: home.
            buttons = new MouseState { buttons = 1 };
            yield return Wait(world.settings.returnHold + .15f);
            buttons = new MouseState();
            Assert.That(world.Returning || game.Phase == GamePhase.Shop, Is.True, "A full hold of LMB goes home.");
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(world.workshop.Model.IsAway, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
