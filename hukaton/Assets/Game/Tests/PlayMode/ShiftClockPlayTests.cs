#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Trading;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The shift clock in the real scene: its time runs only while the player decides (the answers can be pressed). It
    /// stands while the visitor walks in, while his line types, during the «…» after a counter-offer, between visitors and
    /// while the bulk buyer explains himself; standing, the clock is faded (not like the ended shift) and its cross stays
    /// bright. The player's own save files are moved aside for the run and put back afterwards.
    /// </summary>
    public sealed class ShiftClockPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;
        TradingPrototype trade;
        TradingSession session;

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
            // The game itself, without the main menu lying over it.
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            if (session != null) session.CustomerScript = null;
            trade = null; session = null;
            // Unload the game first so nothing writes a save after the files are restored.
            SceneManager.SetActiveScene(SceneManager.CreateScene("Shift clock test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        GameLoop StartInTheShop()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
            trade = game.trade;
            session = trade.Session;
            session.StockEmptySlotsRandomly();
            Assert.That(session.DisplayCount, Is.GreaterThan(0));
            Assert.That(session.RemainingSeconds, Is.EqualTo(session.ShiftLength), "A fresh shift.");
            return game;
        }

        [UnityTest]
        public IEnumerator ShiftTimeRunsOnlyWhileThePlayerDecides()
        {
            StartInTheShop();
            var hud = trade.hud;
            // One staged visitor who asks the price of the first item and answers any price with his last word: the price.
            int slot = Enumerable.Range(0, session.Slots.Count).First(i => session.Slots[i] != null);
            int price = session.Slots[slot].Definition.RecommendedPrice;
            bool staged = false;
            session.CustomerScript = () =>
            {
                if (staged) return null;
                staged = true;
                return new CustomerTerms { Name = "Тест", RequestedSlot = slot, AsksPrice = true, MaxPrice = price * 2, Script = new ScriptedHaggle(1, price, price * 10) };
            };
            float full = session.RemainingSeconds;
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer, Is.Not.Null);

            // He walks in from the door (and the camera moves to the counter view): the time stands.
            float end = Time.realtimeSinceStartup + 10;
            while (!trade.CustomerReady && Time.realtimeSinceStartup < end)
            {
                yield return null;
                Assert.That(session.RemainingSeconds, Is.EqualTo(full), "No time passes while the visitor walks in.");
            }
            Assert.That(trade.CustomerReady, Is.True, "He reaches the counter.");
            Assert.That(trade.ShiftClockRunning, Is.False);
            Assert.That(hud.clockBody.alpha, Is.EqualTo(hud.standingClockAlpha).Within(.001f), "Standing: faded…");
            Assert.That(hud.clockRestingFace.activeSelf, Is.False, "…but not the ended shift's resting face.");
            Assert.That(hud.standingClockAlpha, Is.Not.EqualTo(hud.endedClockAlpha));
            AssertCrossBright(hud);

            // His first line types letter by letter: still standing.
            bool typed = false;
            end = Time.realtimeSinceStartup + 10;
            while (!trade.ShiftClockRunning && Time.realtimeSinceStartup < end)
            {
                typed |= hud.SpeechTyping;
                float before = session.RemainingSeconds;
                yield return null;
                if (!trade.ShiftClockRunning) Assert.That(session.RemainingSeconds, Is.EqualTo(before), "No time passes while his line types.");
            }
            Assert.That(typed, Is.True, "The visitor's line types letter by letter.");
            Assert.That(session.RemainingSeconds, Is.GreaterThan(full - .1f), "The clock starts only once his line is out.");

            // The answers can be pressed: now the time runs, and the clock is bright.
            Assert.That(trade.ActionsReady, Is.True);
            float started = session.RemainingSeconds;
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(trade.ShiftClockRunning, Is.True);
            Assert.That(session.RemainingSeconds, Is.LessThan(started - .4f), "The time runs while the player decides.");
            Assert.That(session.RemainingSeconds, Is.GreaterThan(started - 1.5f));
            Assert.That(hud.clockBody.alpha, Is.EqualTo(1).Within(.001f), "Running: full brightness.");
            AssertCrossBright(hud);

            // A price: he thinks («…»), then his counter types; the time stands all along.
            var deal = session.CurrentDeal;
            Assert.That(deal, Is.Not.Null);
            trade.OfferPrice(deal.Id, deal.Revision, 0);
            Assert.That(session.CurrentDeal, Is.SameAs(deal), "He counters with his last word.");
            Assert.That(deal.N.HasValue, Is.True);
            Assert.That(trade.ReactionRevealed, Is.False, "The «…» beat after a counter-offer.");
            float held = session.RemainingSeconds;
            // The shop decides about the clock in its next Update (this frame it still ran before the price was named).
            yield return null;
            Assert.That(trade.ShiftClockRunning, Is.False, "He thinks: the clock stands.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(held));
            bool thought = false;
            typed = false;
            end = Time.realtimeSinceStartup + 10;
            while (!trade.ShiftClockRunning && Time.realtimeSinceStartup < end)
            {
                thought |= !trade.ReactionRevealed;
                typed |= trade.ReactionRevealed && hud.SpeechTyping;
                yield return null;
                if (!trade.ShiftClockRunning) Assert.That(session.RemainingSeconds, Is.EqualTo(held), "No time passes during «…» and his answer.");
            }
            Assert.That(thought && typed, Is.True, "Both the beat and the typed answer were seen.");
            Assert.That(session.RemainingSeconds, Is.GreaterThan(held - .1f));

            // Sold: nobody at the counter, the time stands until the next visitor can be answered.
            yield return WaitFor(() => trade.ActionsReady, 3);
            trade.AcceptPrice(deal.Id, deal.Revision);
            Assert.That(session.CurrentCustomer, Is.Null, "Sold.");
            held = session.RemainingSeconds;
            end = Time.realtimeSinceStartup + .8f;
            while (Time.realtimeSinceStartup < end && session.IsOpen && !trade.CustomerReady)
            {
                yield return null;
                Assert.That(session.RemainingSeconds, Is.EqualTo(held), "No time passes between visitors.");
            }
            trade.CloseShopNow();
            yield return WaitFor(() => !trade.ViewTransitioning, 3);
            Assert.That(session.RemainingSeconds, Is.GreaterThan(0), "A closed shop keeps the rest of its shift.");
            Assert.That(trade.ShiftClockRunning, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheFirstBulkBuyerExplainsHimselfOnTheHouse()
        {
            StartInTheShop();
            Assert.That(session.BulkBuyerMet, Is.False);
            session.ForceBulkBuyerNext = true;
            float full = session.RemainingSeconds;
            Assert.That(session.Open(), Is.True);
            var buyer = session.CurrentCustomer;
            Assert.That(buyer != null && buyer.IsBulkBuyer && buyer.FirstMeeting, Is.True, "The bulk buyer, met for the first time.");
            Assert.That(buyer.Lines.Count, Is.GreaterThan(1), "The first time he explains himself before his offer.");

            yield return WaitFor(() => trade.ActionsReady, 10);
            Assert.That(trade.ActionsReady, Is.True, "He stands at the counter.");
            Assert.That(trade.BulkBuyerExplaining, Is.True);
            // The player reads his explanation as long as he likes: the shift does not pay for it.
            float end = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                Assert.That(trade.ShiftClockRunning, Is.False);
                Assert.That(session.RemainingSeconds, Is.EqualTo(full), "No time passes while he explains himself.");
            }
            Assert.That(trade.hud.clockBody.alpha, Is.EqualTo(trade.hud.standingClockAlpha).Within(.001f));
            AssertCrossBright(trade.hud);
            trade.RefuseCustomer(buyer.Id);
            Assert.That(session.CurrentCustomer, Is.Null, "Refused.");
            trade.CloseShopNow();
            yield return WaitFor(() => !trade.ViewTransitioning, 3);
            Assert.That(session.RemainingSeconds, Is.EqualTo(full));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheLineAboutTheClockComesOnceAfterTheTutorial()
        {
            var game = StartInTheShop();
            Assert.That(game.ShiftClockLineGiven, Is.False, "A new game has not been told.");
            Assert.That(game.shiftClockLine, Is.EqualTo("Часы идут, только пока ты думаешь над ценой."));
            Assert.That(session.Open(), Is.True);
            yield return null; // the game loop notices the opening in its LateUpdate
            yield return null;
            Assert.That(trade.Notice, Is.EqualTo(game.shiftClockLine), "The first own shift says how the clock runs.");
            Assert.That(game.ShiftClockLineGiven, Is.True);
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.shiftClockLineGiven, Is.True, "Saved: it never comes again.");

            trade.CloseShopNow();
            yield return WaitFor(() => !trade.ViewTransitioning, 3);
            trade.ShowNotice(string.Empty);
            Assert.That(session.Open(), Is.True);
            yield return null;
            yield return null;
            Assert.That(trade.Notice, Is.Not.EqualTo(game.shiftClockLine), "Once.");
            trade.CloseShopNow();
            yield return WaitFor(() => !trade.ViewTransitioning, 3);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AnAnswerCostsAtLeastTheFloorFromTheStartOfItsLine()
        {
            StartInTheShop();
            var hud = trade.hud;
            Assert.That(trade.minAnswerSeconds, Is.EqualTo(2), "The scene's floor of an answer.");
            float floor = trade.minAnswerSeconds;
            // One staged visitor who asks the price of the first item and answers any price with his last word: the price.
            int slot = Enumerable.Range(0, session.Slots.Count).First(i => session.Slots[i] != null);
            int price = session.Slots[slot].Definition.RecommendedPrice;
            bool staged = false;
            session.CustomerScript = () =>
            {
                if (staged) return null;
                staged = true;
                return new CustomerTerms { Name = "Тест", RequestedSlot = slot, AsksPrice = true, MaxPrice = price * 2, Script = new ScriptedHaggle(1, price, price * 10) };
            };
            float full = session.RemainingSeconds;
            Assert.That(session.Open(), Is.True);

            // A price pressed while his first line still types: reading was free, the answer pays the whole floor.
            yield return WaitFor(() => trade.ActionsReady && hud.SpeechTyping, 10);
            Assert.That(trade.ActionsReady && hud.SpeechTyping, Is.True, "His line types and the answers can be pressed.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(full));
            var deal = session.CurrentDeal;
            trade.OfferPrice(deal.Id, deal.Revision, 0);
            Assert.That(session.CurrentDeal, Is.SameAs(deal), "He counters with his last word.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(full - floor).Within(1e-4f), "Exactly the floor.");
            // The clock shows the jump honestly: the fill stands at the new time on the next frame, not eased to it.
            yield return null;
            Assert.That(hud.clockFill.fillAmount,
                Is.EqualTo(TradingHud.ClockFill(session.RemainingSeconds, session.ShiftLength)).Within(1e-4f));

            // His counter: three seconds of deciding once it has typed cost those three seconds, nothing on top.
            float lineStart = session.RemainingSeconds;
            yield return WaitFor(() => trade.ShiftClockRunning, 10);
            yield return WaitFor(() => session.RemainingSeconds <= lineStart - 3, 6);
            Assert.That(session.RemainingSeconds, Is.LessThanOrEqualTo(lineStart - 3), "Three seconds spent deciding.");
            float before = session.RemainingSeconds;
            trade.AcceptPrice(deal.Id, deal.Revision);
            Assert.That(session.CurrentCustomer, Is.Null, "Sold.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(before), "Nothing on top of the time spent deciding.");
            Assert.That(session.LastAnswerFloorCharge, Is.Zero);
            trade.CloseShopNow();
            yield return WaitFor(() => !trade.ViewTransitioning, 3);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheBulkBuyersOfferHasTheFloorButNotHisExplanation()
        {
            StartInTheShop();
            float floor = trade.minAnswerSeconds;
            // The first meeting: refused while he still explains himself, which costs nothing.
            session.ForceBulkBuyerNext = true;
            float full = session.RemainingSeconds;
            Assert.That(session.Open(), Is.True);
            var buyer = session.CurrentCustomer;
            yield return WaitFor(() => trade.ActionsReady, 10);
            Assert.That(trade.BulkBuyerExplaining, Is.True);
            trade.RefuseCustomer(buyer.Id);
            Assert.That(session.CurrentCustomer, Is.Null, "Refused.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(full), "His explanation is free, even refused.");

            // Met: next time his offer is his first line, and an answer pressed at once pays the floor.
            session.ForceBulkBuyerNext = true;
            yield return WaitFor(() => session.CurrentCustomer != null && trade.ActionsReady, 10);
            buyer = session.CurrentCustomer;
            Assert.That(buyer != null && buyer.IsBulkBuyer && !buyer.FirstMeeting && trade.BulkOfferShown, Is.True, "His offer at once.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(full));
            trade.RefuseCustomer(buyer.Id);
            Assert.That(session.CurrentCustomer, Is.Null, "Refused.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(full - floor).Within(1e-4f), "The answer to his offer pays the floor.");
            trade.CloseShopNow();
            yield return WaitFor(() => !trade.ViewTransitioning, 3);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The red cross that closes the shop is shown, pressable and never faded with the clock's body.</summary>
        void AssertCrossBright(TradingHud hud)
        {
            Assert.That(hud.clockClose.gameObject.activeInHierarchy, Is.True, "The cross is there while the shop is open.");
            float alpha = 1;
            foreach (var group in hud.clockClose.GetComponentsInParent<CanvasGroup>()) alpha *= group.alpha;
            Assert.That(alpha, Is.EqualTo(1).Within(.001f), "The cross stays bright.");
            Assert.That(hud.clockClose.interactable || trade.ViewTransitioning, Is.True);
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
