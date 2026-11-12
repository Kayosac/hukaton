#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Hakaton.Backroom.Tests
{
    /// <summary>
    /// The goblin's table under a pointer that rests near a card's bottom edge (playtest: the card under the pointer
    /// jittered without end). Cards read the pointer from their still slots, so a card that lifts and grows cannot
    /// slide out from under the pointer, and the slot reaches up after the lifted card while it is up.
    /// The table is the authored prefab on its own overlay canvas; the test moves its own pointer.
    /// </summary>
    public sealed class GoblinCardHoverTests
    {
        const string TablePath = "Assets/Backroom/Prefabs/GoblinCardTable.prefab";
        const string BrazierCardPath = "Assets/Backroom/Prefabs/BrazierCard.prefab";

        GameObject fixture;
        GoblinCardShop shop;
        GraphicRaycaster raycaster;
        EventSystem events;
        readonly List<RaycastResult> hits = new List<RaycastResult>();

        [UnitySetUp]
        public IEnumerator LayTheAuthoredTable()
        {
            fixture = new GameObject("Goblin table fixture");
            // Raycasts only: an event system without an input module, so a real mouse over the Game view cannot send
            // its own enters and exits into the test.
            events = fixture.AddComponent<EventSystem>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TablePath);
            Assert.That(prefab, Is.Not.Null, TablePath);
            var table = Object.Instantiate(prefab, fixture.transform);
            shop = table.GetComponent<GoblinCardShop>();
            raycaster = table.GetComponent<GraphicRaycaster>();
            // The shop keeps the table hidden until the goblin is down; shown here as Open shows it, with a fresh ten
            // dealt face down (no collection or goblin needed for the pointer).
            shop.visibility.alpha = 1;
            shop.visibility.interactable = shop.visibility.blocksRaycasts = true;
            foreach (var card in shop.cards) card.Deal(null, false, 0);
            yield return Settle(.8f);
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            Object.Destroy(fixture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryCardReadsThePointerFromItsAuthoredStillSlot()
        {
            // The prefab asset itself: the hit areas are authored, not made at run time.
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(TablePath).GetComponent<GoblinCardShop>();
            Assert.That(authored.cards, Is.Not.Empty);
            foreach (var card in authored.cards)
            {
                var catching = card.body.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget).Select(g => g.name);
                Assert.That(catching, Is.Empty, $"{card.name}: nothing on the moving body catches the pointer");
                Assert.That(card.hitArea, Is.Not.Null, $"{card.name}: has a hit area");
                Assert.That(card.hitArea.gameObject, Is.SameAs(card.gameObject), $"{card.name}: the hit area is the still slot");
                Assert.That(card.hitArea.raycastTarget, Is.True, $"{card.name}: the slot catches the pointer");
                Assert.That(card.hitArea.color.a, Is.Zero, $"{card.name}: the slot draws nothing");
                Assert.That(card.hitArea.raycastPadding, Is.EqualTo(Vector4.zero), $"{card.name}: at rest the slot is the card's own size");
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PointerAtTheBottomEdgeKeepsTheCardItLifted()
        {
            var card = shop.cards[shop.cards.Length / 2 - 1];
            var slot = (RectTransform)card.transform;
            var bottom = OnScreen(slot, new Vector2(0, slot.rect.yMin + 6));
            Assert.That(CardAt(OnScreen(slot, slot.rect.center)), Is.SameAs(card), "the raycaster finds the card under its middle");
            Assert.That(CardAt(bottom), Is.SameAs(card), "and near its bottom edge");

            float rest = card.body.anchoredPosition.y;
            ExecuteEvents.Execute(card.gameObject, Pointer(bottom), ExecuteEvents.pointerEnterHandler);
            yield return Settle(.6f);
            Assert.That(card.Hovering, Is.True);
            Assert.That(card.body.anchoredPosition.y - rest, Is.GreaterThan(card.hoverLift * .8f), "the card is up");
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(card.body, bottom, null), Is.False,
                "the lifted card has left the pointer below it (the band where it used to flicker)");
            Assert.That(CardAt(bottom), Is.SameAs(card), "the still slot keeps the card under the pointer");

            // Following the lifted card up: just under its top edge, over the slot's own top.
            var top = OnScreen(card.body, new Vector2(0, card.body.rect.yMax - 4));
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(slot, top, null), Is.False, "above the slot itself");
            Assert.That(CardAt(top), Is.SameAs(card), "the slot reaches up after the lifted card");

            // Once left, the reach is gone: the same spot does not take the card back, so it cannot bob there either.
            ExecuteEvents.Execute(card.gameObject, Pointer(top), ExecuteEvents.pointerExitHandler);
            Assert.That(card.Hovering, Is.False);
            Assert.That(CardAt(top), Is.Not.SameAs(card), "only the slot itself lets the pointer in");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StillPointerInTheBottomBandDoesNotMakeTheCardBob()
        {
            var card = shop.cards[shop.cards.Length / 2 - 1];
            var slot = (RectTransform)card.transform;
            var at = OnScreen(slot, new Vector2(0, slot.rect.yMin + 6));
            int hovered = 0, unhovered = 0;
            card.Hovered += _ => hovered++;
            card.Unhovered += _ => unhovered++;
            // The input module's part, frame by frame: the topmost card under the resting pointer gets the enter and
            // loses it (exit) only when the raycast stops finding it.
            OfferCardView under = null;
            int changes = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                var now = CardAt(at);
                if (now != under)
                {
                    var data = Pointer(at);
                    if (under) ExecuteEvents.Execute(under.gameObject, data, ExecuteEvents.pointerExitHandler);
                    if (now) ExecuteEvents.Execute(now.gameObject, data, ExecuteEvents.pointerEnterHandler);
                    under = now;
                    changes++;
                }
                // About a 60 fps frame each, so the card has time to lift (and, before the fix, to leave the pointer).
                yield return new WaitForSecondsRealtime(1 / 60f);
            }
            Assert.That(changes, Is.LessThanOrEqualTo(1), "one enter, then nothing");
            Assert.That(under, Is.SameAs(card));
            Assert.That(card.Hovering, Is.True);
            Assert.That(hovered, Is.EqualTo(1), "the price comes up once");
            Assert.That(unhovered, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BrazierTileMakesItsStillSlotOnceAndLetsTheReachGo()
        {
            // The brazier lays its cards from a tile prefab and makes their hit areas as it lays them.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrazierCardPath);
            Assert.That(prefab, Is.Not.Null, BrazierCardPath);
            var tile = Object.Instantiate(prefab, shop.cards[0].transform.parent).GetComponent<OfferCardView>();
            tile.UseStillHitArea();
            // Laid again on the next opening.
            tile.UseStillHitArea();
            Assert.That(tile.GetComponents<Graphic>().Length, Is.EqualTo(1), "one hit area on the still slot");
            Assert.That(tile.hitArea, Is.SameAs(tile.GetComponent<Graphic>()));
            Assert.That(tile.body.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget), Is.Empty);
            tile.Deal(null, true, 0);
            yield return null;

            var data = new PointerEventData(events);
            void Enter()
            {
                ExecuteEvents.Execute(tile.gameObject, data, ExecuteEvents.pointerEnterHandler);
                Assert.That(tile.hitArea.raycastPadding.w, Is.LessThan(-tile.hoverLift), "reaches over the lifted card");
            }
            void LetGo(string why)
            {
                Assert.That(tile.Hovering, Is.False, why);
                Assert.That(tile.hitArea.raycastPadding, Is.EqualTo(Vector4.zero), why);
            }

            Enter();
            ExecuteEvents.Execute(tile.gameObject, data, ExecuteEvents.pointerExitHandler);
            LetGo("left");
            Enter();
            tile.Deal(null, true, 0);
            LetGo("dealt again");
            Enter();
            tile.SetGuarded(true, null, "защищено");
            LetGo("guarded");
            tile.Deal(null, true, 0);
            Enter();
            tile.FlyAway();
            LetGo("flown off");
            tile.Deal(null, true, 0);
            Enter();
            tile.Hide();
            LetGo("put away");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- the test's pointer ----------

        /// <summary>A point local to a rect, on screen (the table's overlay canvas has no camera).</summary>
        static Vector2 OnScreen(RectTransform rect, Vector2 local) =>
            RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local));

        PointerEventData Pointer(Vector2 at) => new PointerEventData(events) { position = at };

        /// <summary>The card under the pointer as the input module would find it: the topmost hit of the table's raycaster.</summary>
        OfferCardView CardAt(Vector2 at)
        {
            hits.Clear();
            raycaster.Raycast(Pointer(at), hits);
            return hits.Count == 0 ? null : hits[0].gameObject.GetComponentInParent<OfferCardView>();
        }

        static IEnumerator Settle(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
            yield return null;
        }
    }
}
#endif
