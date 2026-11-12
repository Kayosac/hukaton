using System.Collections;
using Hakaton.Decks;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Dummy cards («Пустышка», bought blind from the goblin) do not go on an outing: <c>DeckWorkshop.Depart</c> takes
    /// them out of the deck and the collection for good and lists them in <c>Expedition.Dummies</c>. As the hand is
    /// dealt, each one rises out of the middle of the fan and is flung forward and away, spinning, one after another,
    /// and fizzles out. Purely presentation: the outing's deck (and the HUD count) already left without them.
    /// </summary>
    public sealed partial class ExpeditionWorld
    {
        /// <summary>Seconds after the deal before the first dummy goes, between dummies, and each one's flight.</summary>
        const float DummyDelay = .35f, DummyStagger = .16f, DummyFlight = .75f;

        /// <summary>Flings away the dummies the deck shed for the outing that just began.</summary>
        void ShedDummies()
        {
            var dummies = workshop.Model.Current?.Dummies;
            if (dummies == null || dummies.Count == 0 || !debrisPrefab || !hand) return;
            for (int i = 0; i < dummies.Count; i++)
                StartCoroutine(FlingDummy(dummies[i], DummyDelay + i * DummyStagger, i - (dummies.Count - 1) * .5f));
        }

        IEnumerator FlingDummy(CardCopy card, float delay, float side)
        {
            yield return new WaitForSeconds(delay);
            if (!Running || !hand || !viewCamera) yield break;
            var root = hand.transform;
            var eye = viewCamera.transform;
            // From just under the middle of the fan, where the dealt cards rise to, so it comes up with the deal.
            var local = hand.pivot + new Vector3(side * hand.cardWidth * .35f, hand.radius - hand.cardWidth, -.02f);
            var position = root.TransformPoint(local);
            var debris = Instantiate(debrisPrefab, position, root.rotation * Quaternion.Euler(0, 0, -side * 8), spawnedObjects);
            debris.view.Show(Artwork(card), hand.cardBack, card.Level);
            debris.view.transform.localScale = Vector3.Scale(root.lossyScale,
                new Vector3(hand.cardWidth, hand.cardWidth * CardHandView.CardAspect, 1));
            // Forward and up, out of the frame, tumbling.
            var velocity = eye.forward * 7f + eye.up * 3.2f + eye.right * (side * 1.2f);
            var spin = new Vector3(Random.Range(-260f, -140f), Random.Range(-200f, 200f), (side >= 0 ? -1 : 1) * Random.Range(500f, 800f));
            debris.Fling(this, velocity, spin, DummyFlight);
            Play(cues ? cues.cardThrow : null, position, .7f, Random.Range(1.05f, 1.25f));
            yield return new WaitForSeconds(DummyFlight * .8f);
            if (debris) Spawn(vfx ? vfx.paperPoof : null, debris.transform.position, Quaternion.identity, Accent(card), .6f);
        }
    }
}
