#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Character;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Hakaton.ShopBlockout.Tests
{
    public sealed class EmbeddedWorkshopTests
    {
        GameObject fixture;
        DeckPrototype workshop;

        [UnitySetUp]
        public IEnumerator OpenAnIsolatedAuthoredWorkshopWithoutTouchingPlayerSaves()
        {
            fixture = new GameObject("Embedded workshop fixture");
            fixture.SetActive(false);
            // The isolated UI fixture plays its real purchase sounds without loading a player camera.
            if (!Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Any(listener => listener.isActiveAndEnabled))
                fixture.AddComponent<AudioListener>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decks/Prefabs/DeckWorkshop.prefab");
            var instance = Object.Instantiate(prefab, fixture.transform);
            workshop = instance.GetComponent<DeckPrototype>();
            workshop.walletHost = null;
            // The game funds the workshop from the shared wallet; the isolated fixture brings its own coins.
            workshop.config = Object.Instantiate(workshop.config);
            workshop.config.startingCoins = 1000;
            workshop.control.captureOnStart = false;
            workshop.embeddedVisibility = instance.AddComponent<CanvasGroup>();
            var close =
                new GameObject("Test close button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            close.transform.SetParent(instance.transform, false);
            workshop.embeddedCloseButton = close.GetComponent<Button>();
            foreach (var input in instance.GetComponentsInChildren<EventSystem>(true)) input.gameObject.SetActive(false);
            fixture.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            Object.Destroy(workshop.config);
            Object.Destroy(fixture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClosedServiceDoesNotTakeControlOrSpendAndCannotStartAnExpedition()
        {
            Assert.That(workshop.EmbeddedOpen, Is.False);
            Assert.That(workshop.control.HasModalOwner, Is.False);
            Assert.That(workshop.embeddedVisibility.alpha, Is.Zero);
            workshop.Model.Buy("rope", out var a);
            workshop.Model.Buy("rope", out _);
            int coins = workshop.Model.Coins, owned = workshop.Model.Owned.Count;
            workshop.SelectCopy(a.Id);
            workshop.MergeSelected();
            workshop.UpgradeHand();
            workshop.UpgradeDeckLimit();
            workshop.TryDepart();
            Assert.That(workshop.Model.Coins, Is.EqualTo(coins));
            Assert.That(workshop.Model.Owned.Count, Is.EqualTo(owned), "Nothing merges while the PC is closed.");
            Assert.That(workshop.Model.IsAway, Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ComputerMergesTheOnlyPairAtOnceAndForFree()
        {
            Assert.That(workshop.OpenEmbedded(), Is.True);
            workshop.Model.Buy("rope", out var a);
            workshop.Model.Buy("rope", out var b);
            int coins = workshop.Model.Coins;
            workshop.SelectCopy(a.Id);
            workshop.mergeButton.onClick.Invoke();
            Assert.That(workshop.Model.Find(a.Id).Level, Is.EqualTo(2));
            Assert.That(workshop.Model.Find(b.Id), Is.Null);
            Assert.That(workshop.Model.Coins, Is.EqualTo(coins));
            Assert.That(workshop.control.HasModalOwner, Is.True);
            workshop.embeddedCloseButton.onClick.Invoke();
            Assert.That(workshop.EmbeddedOpen, Is.False);
            Assert.That(workshop.control.HasModalOwner, Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WithSeveralPartnersThePlayerPicksTheSecondCopy()
        {
            workshop.OpenEmbedded();
            var copies = new CardCopy[3];
            for (int i = 0; i < 3; i++) workshop.Model.Buy("fog", out copies[i]);
            workshop.SelectCopy(copies[0].Id);
            workshop.MergeSelected();
            Assert.That(workshop.MergeSource, Is.EqualTo(copies[0].Id), "Waiting for the second copy.");
            Assert.That(workshop.Model.Owned.Count(c => c.Spec.Id == "fog"), Is.EqualTo(3));
            workshop.SelectCopy(copies[2].Id);
            Assert.That(workshop.MergeSource, Is.Zero);
            Assert.That(workshop.Model.Find(copies[0].Id).Level, Is.EqualTo(2));
            Assert.That(workshop.Model.Find(copies[2].Id), Is.Null);
            Assert.That(workshop.Model.Find(copies[1].Id).Level, Is.EqualTo(1));
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosingOrDisablingServiceReleasesOnlyItsOwnControlClaim()
        {
            workshop.OpenEmbedded();
            workshop.control.SetBlocked(fixture, true);
            workshop.CloseEmbedded();
            Assert.That(workshop.control.HasModalOwner, Is.True);
            workshop.control.SetBlocked(fixture, false);
            Assert.That(workshop.control.HasModalOwner, Is.False);
            workshop.OpenEmbedded();
            workshop.enabled = false;
            Assert.That(workshop.EmbeddedOpen, Is.False);
            Assert.That(workshop.control.HasModalOwner, Is.False);
            Assert.That(workshop.embeddedVisibility.blocksRaycasts, Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
