#if UNITY_EDITOR
using System.Collections;
using System.Linq;
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
            fixture=new GameObject("Embedded workshop fixture");
            fixture.SetActive(false);
            // The isolated UI fixture plays its real purchase sounds without loading a player camera.
            if(!Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Any(listener=>listener.isActiveAndEnabled))
                fixture.AddComponent<AudioListener>();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decks/Prefabs/DeckWorkshop.prefab");
            var instance=Object.Instantiate(prefab,fixture.transform);
            workshop=instance.GetComponent<DeckPrototype>();
            workshop.embeddedMode=true;
            workshop.persistProgress=false;
            workshop.walletHost=null;
            workshop.control.captureOnStart=false;
            workshop.embeddedVisibility=instance.AddComponent<CanvasGroup>();
            var close=new GameObject("Test close button",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));
            close.transform.SetParent(instance.transform,false);
            workshop.embeddedCloseButton=close.GetComponent<Button>();
            foreach(var input in instance.GetComponentsInChildren<EventSystem>(true))input.gameObject.SetActive(false);
            fixture.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp() {Object.Destroy(fixture);yield return null;}

        [UnityTest]
        public IEnumerator ClosedServiceDoesNotTakeControlOrSpendAndCannotStartAnExpedition()
        {
            Assert.That(workshop.EmbeddedOpen,Is.False);
            Assert.That(workshop.control.HasModalOwner,Is.False);
            Assert.That(workshop.embeddedVisibility.alpha,Is.Zero);
            int coins=workshop.Model.Coins;
            int level=workshop.Model.Find(workshop.SelectedCopyId).Level;
            workshop.UpgradeSelected();workshop.UpgradeHand();workshop.Depart();
            Assert.That(workshop.Model.Coins,Is.EqualTo(coins));
            Assert.That(workshop.Model.Find(workshop.SelectedCopyId).Level,Is.EqualTo(level));
            Assert.That(workshop.Model.IsAway,Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ComputerUpgradesAndGoblinPurchasesUseTheSameCollection()
        {
            Assert.That(workshop.OpenEmbedded(false),Is.True);
            int copyId=workshop.SelectedCopyId;
            workshop.upgradeButton.onClick.Invoke();
            Assert.That(workshop.Model.Find(copyId).Level,Is.EqualTo(2));
            Assert.That(workshop.control.HasModalOwner,Is.True);
            workshop.embeddedCloseButton.onClick.Invoke();
            Assert.That(workshop.EmbeddedOpen,Is.False);
            Assert.That(workshop.control.HasModalOwner,Is.False);

            Assert.That(workshop.OpenEmbedded(true),Is.True);
            Assert.That(workshop.Market,Is.True);
            int owned=workshop.Model.Owned.Count;
            workshop.equipButton.onClick.Invoke();
            Assert.That(workshop.Model.Owned.Count,Is.EqualTo(owned+1));
            Assert.That(workshop.Market,Is.True,"Buying from the goblin must keep the card offers visible.");
            Assert.That(workshop.Model.Find(copyId).Level,Is.EqualTo(2));
            int coins=workshop.Model.Coins;
            workshop.UpgradeHand();workshop.ChooseMarket(false);workshop.Depart();
            Assert.That(workshop.Model.Coins,Is.EqualTo(coins));
            Assert.That(workshop.Market,Is.True);
            Assert.That(workshop.Model.IsAway,Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosingOrDisablingServiceReleasesOnlyItsOwnControlClaim()
        {
            workshop.OpenEmbedded(false);
            workshop.control.SetBlocked(fixture,true);
            workshop.CloseEmbedded();
            Assert.That(workshop.control.HasModalOwner,Is.True);
            workshop.control.SetBlocked(fixture,false);
            Assert.That(workshop.control.HasModalOwner,Is.False);
            workshop.OpenEmbedded(true);
            workshop.enabled=false;
            Assert.That(workshop.EmbeddedOpen,Is.False);
            Assert.That(workshop.control.HasModalOwner,Is.False);
            Assert.That(workshop.embeddedVisibility.blocksRaycasts,Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
