#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Hakaton.Character;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Trading.Tests.PlayMode
{
    public sealed class TradingCustomerPlayModeTests
    {
        TradingPrototype trade;
        SpriteNpcActor actor;

        [UnitySetUp]
        public IEnumerator LoadSavedTradingScene()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Trading/Scenes/TradingPrototype.unity", LoadSceneMode.Single);
            trade = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<TradingPrototype>(true)).Single();
            actor = trade.World.Customers.actor;
            actor.walkSpeed = 12; actor.reactionDuration = .25f; actor.turnDuration = .2f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SuccessfulSaleShowsHappyThenBackAndWaitsBeforeNextVisitor()
        {
            trade.Session.PlaceItem(0, 0); trade.Session.PlaceItem(0, 1);
            Assert.That(trade.Session.Open(), Is.True);
            yield return WaitFor(() => actor.State == NpcActorState.Arriving);
            Assert.That(trade.ActionsReady, Is.False);
            Assert.That(trade.hud.interactionPanel.gameObject.activeSelf, Is.False);
            yield return WaitFor(() => trade.ActionsReady);
            long customerId = trade.Session.CurrentCustomer.Id;
            var appearance = actor.CurrentAppearance;
            yield return SellCurrentItem();
            Assert.That(trade.Session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Reacting));
            Assert.That(actor.spriteRenderer.sprite, Is.EqualTo(appearance.happy));
            Assert.That(trade.Session.CurrentCustomer, Is.Null);
            yield return WaitFor(() => actor.State == NpcActorState.Leaving);
            Assert.That(actor.spriteRenderer.sprite, Is.EqualTo(appearance.back));
            Assert.That(actor.spriteRenderer.enabled, Is.True);
            Assert.That(trade.Session.CurrentCustomer, Is.Null);
            yield return WaitFor(() => trade.Session.CurrentCustomer != null && trade.Session.CurrentCustomer.Id != customerId);
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Arriving));
            Assert.That(actor.CurrentAppearance, Is.Not.EqualTo(appearance));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RefusedVisitorIsAngryAndKeepsTheUnsoldItem()
        {
            trade.Session.PlaceItem(0, 0); trade.Session.Open();
            yield return WaitFor(() => trade.ActionsReady);
            var item = trade.Session.Slots[0];
            trade.RefuseCustomer(trade.Session.CurrentCustomer.Id);
            Assert.That(actor.CurrentExpression, Is.EqualTo(NpcExpression.Angry));
            Assert.That(actor.spriteRenderer.sprite, Is.EqualTo(actor.CurrentAppearance.angry));
            Assert.That(trade.Session.Slots[0], Is.SameAs(item));
            Assert.That(trade.Session.Gold, Is.Zero);
            yield return WaitFor(() => actor.State == NpcActorState.Leaving);
            Assert.That(actor.spriteRenderer.sprite, Is.EqualTo(actor.CurrentAppearance.back));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LastSaleClosesShopButLetsTheHappyVisitorFinishLeaving()
        {
            trade.Session.PlaceItem(0, 0); trade.Session.Open();
            yield return WaitFor(() => trade.ActionsReady);
            yield return SellCurrentItem();
            Assert.That(trade.Session.IsOpen, Is.False);
            Assert.That(actor.State, Is.EqualTo(NpcActorState.Reacting));
            Assert.That(actor.CurrentExpression, Is.EqualTo(NpcExpression.Happy));
            Assert.That(actor.spriteRenderer.enabled, Is.True);
            yield return WaitFor(() => !actor.IsBusy);
            Assert.That(actor.spriteRenderer.enabled, Is.False);
            Assert.That(actor.transform.position, Is.EqualTo(actor.route[0].position));
            Assert.That(trade.Session.CurrentCustomer, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosingDuringArrivalTurnsVisitorWithoutTeleporting()
        {
            trade.Session.PlaceItem(0, 0); trade.Session.Open();
            yield return WaitFor(() => actor.State == NpcActorState.Arriving);
            yield return new WaitForSeconds(.12f);
            Vector3 before = actor.transform.position;
            trade.ToggleShop();
            Assert.That(actor.transform.position, Is.EqualTo(before));
            Assert.That(actor.CurrentExpression, Is.EqualTo(NpcExpression.Angry));
            Assert.That(trade.Session.Slots[0], Is.Not.Null);
            yield return WaitFor(() => !actor.IsBusy);
            Assert.That(trade.Session.IsOpen, Is.False);
            Assert.That(actor.transform.position, Is.EqualTo(actor.route[0].position));
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator SellCurrentItem()
        {
            if (trade.Session.CurrentDeal == null)
            {
                var customer = trade.Session.CurrentCustomer;
                int slot = Enumerable.Range(0, trade.Session.Slots.Count).First(i =>
                    trade.Session.Slots[i] != null && trade.Session.Slots[i].Definition.Category == customer.RequestedCategory);
                trade.SelectCustomerItem(slot, customer.Id);
                yield return WaitFor(() => trade.ActionsReady);
            }
            var deal = trade.Session.CurrentDeal;
            if (deal.N.HasValue) trade.AcceptPrice(deal.Id, deal.Revision);
            else trade.OfferPrice(deal.Id, deal.Revision, 0);
        }

        static IEnumerator WaitFor(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for the authored customer lifecycle.");
        }
    }
}
#endif
