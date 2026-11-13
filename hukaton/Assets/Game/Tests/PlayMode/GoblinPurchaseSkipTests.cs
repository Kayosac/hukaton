#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hakaton.Backroom;
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
    /// Skipping the goblin's bought card (user rule 2026-11-13): a skip while the card is on its way up skips only the
    /// flight, the card is at the eyes at once (a blind card turned) so the player sees what he bought; the next press
    /// puts it away. A skip once it is at the eyes puts it away at once. In the real scene, with its own keyboard and
    /// mouse; the player's save files are moved aside for the run and put back afterwards.
    /// </summary>
    public sealed class GoblinPurchaseSkipTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;
        Keyboard keyboard;
        Mouse mouse;

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
            float waited = 0;
            while (!(Object.FindAnyObjectByType<GameLoop>()?.Ready ?? false) && waited < 8) { waited += Time.unscaledDeltaTime; yield return null; }
            keyboard = InputSystem.AddDevice<Keyboard>("Skip test keyboard");
            keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("Skip test mouse");
            mouse.MakeCurrent();
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Goblin skip test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator SkipOnTheWayUpStopsAtTheEyesAndTheNextPressPutsItAway([Values(false, true)] bool byMouse)
        {
            var shop = OpenTheTable();
            yield return BuyAfterHisIntroductions(shop);
            yield return new WaitForSecondsRealtime(.2f);
            var card = Object.FindAnyObjectByType<CardInspect>();
            Assert.That(card, Is.Not.Null, "the bought card comes up to the eyes");
            Assert.That(card.Arrived, Is.False, "still on its way up");

            yield return Skip(byMouse);
            Assert.That(card != null, Is.True, "the first skip leaves the card in view");
            Assert.That(card.Arrived, Is.True, "it is at the eyes at once");
            Assert.That(card.Leaving, Is.False);
            Assert.That(shop.Inspecting, Is.True);
            for (int frame = 0; frame < 20; frame++) yield return null;
            Assert.That(card != null && card.Arrived && !card.Leaving, Is.True, "it stays there, the click's release does not send it away");

            yield return Skip(byMouse);
            yield return null;
            Assert.That(card == null, Is.True, "the next press puts it away");
            Assert.That(shop.Inspecting, Is.False);
        }

        [UnityTest]
        public IEnumerator SkipAtTheEyesPutsItAwayAtOnce()
        {
            var shop = OpenTheTable();
            yield return BuyAfterHisIntroductions(shop);
            yield return new WaitForSecondsRealtime(1.2f);
            var card = Object.FindAnyObjectByType<CardInspect>();
            Assert.That(card != null && card.Arrived, Is.True, "at the eyes");
            yield return Skip(false);
            Assert.That(card == null, Is.True);
            Assert.That(shop.Inspecting, Is.False);
        }

        static GoblinCardShop OpenTheTable()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            var shop = game.services.cardShop;
            game.workshop.Model.Wallet.Credit(100000);
            Assert.That(shop.TryCall(5), Is.True, "he comes down");
            Assert.That(shop.Open(), Is.True, "the table is laid");
            return shop;
        }

        /// <summary>He talks first (the table, then each special item on it, lines closed by E); then a card is bought.</summary>
        IEnumerator BuyAfterHisIntroductions(GoblinCardShop shop)
        {
            bool bought = false;
            for (int guard = 0; !bought && guard < 60; guard++)
            {
                yield return new WaitForSecondsRealtime(.05f);
                bought = shop.Choose(FirstUnsold(shop));
                if (!bought) { yield return Press(); yield return Press(); }
            }
            Assert.That(bought, Is.True, "bought once he has had his say");
        }

        static int FirstUnsold(GoblinCardShop shop)
        {
            int slot = 0;
            while (shop.Visits.Offer.IsSold(slot)) slot++;
            return slot;
        }

        IEnumerator Skip(bool byMouse) => byMouse ? Click() : Press();

        IEnumerator Press()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        IEnumerator Click()
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(5, 5) }.WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(5, 5) });
            yield return null;
        }
    }
}
#endif
