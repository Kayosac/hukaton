using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Правила «Бумеранга»: числа таблицы; полёт разворачивается на дальности и возвращается к игроку (одинаково при
    /// любом шаге — превью не врёт), не кружит, без поимки падает по времени; одно существо ранится не чаще раза на
    /// каждом отрезке; поймали — та же копия снова в руке, не поймали — в сброс; стена останавливает, существо — нет.
    /// </summary>
    public sealed class BoomerangTests
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var go in spawned) if (go) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        static CardDefinition Card()
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            var card = config.Find("boomerang");
            Assert.That(card, Is.Not.Null, "Бумеранг есть в каталоге.");
            return card;
        }

        static BoomerangEffect Effect()
        {
            var effect = Card().effectAsset as BoomerangEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        static BoomerangPath Throw(BoomerangEffect effect, int level, Vector3 velocity)
            => BoomerangRules.Begin(Vector3.zero, velocity, effect.range.At(level), effect.minimumSpeed, effect.maximumSpeed,
                effect.curlDegrees, effect.turnDegreesPerSecond, effect.maximumPitch, effect.spareSeconds);

        /// <summary>Летит шагами физики, пока не поймали или не вышло время; игрок стоит в <paramref name="player"/>.</summary>
        static bool Fly(BoomerangEffect effect, ref BoomerangPath path, Vector3 player, out float farthest, out float leftmost)
        {
            farthest = 0;
            leftmost = 0;
            Vector3 bottom = player + Vector3.up * .5f, top = player + Vector3.up * 1.3f, chest = player + Vector3.up * 1.1f;
            for (int i = 0; i < 2000; i++)
            {
                BoomerangRules.Advance(ref path, chest, 1f / 50f);
                farthest = Mathf.Max(farthest, Vector3.Dot(path.Position - path.Origin, path.Forward));
                leftmost = Mathf.Min(leftmost, path.Position.x);
                if (BoomerangRules.Caught(path, bottom, top, effect.catchRadius)) return true;
                if (BoomerangRules.TimedOut(path)) return false;
            }
            return false;
        }

        [Test]
        public void CardHasTheTablesNumbers()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.Boomerang));
            Assert.That((int)CardAction.Boomerang, Is.EqualTo(41));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((2, 170)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
            Assert.That(card.description, Is.EqualTo("Ранит на пути туда и обратно. Поймай — вернётся в руку."));
            var effect = Effect();
            Assert.That((effect.damage.i, effect.damage.ii, effect.damage.iii), Is.EqualTo((2, 3, 5)));
            Assert.That((effect.range.i, effect.range.ii, effect.range.iii), Is.EqualTo((10f, 12f, 15f)));
            Assert.That(effect.TryGetStat(out var label, out var values, out _), Is.True);
            Assert.That((label, values.i, values.iii), Is.EqualTo(("Урон", 2f, 5f)));
            Assert.That(effect is ICustomFlight flight && flight.MayComeBack, Is.True, "Бумеранг летит по своей траектории и может вернуться.");
            Assert.That(effect.impactVfx, Is.Not.Null, "Искры попаданий.");
            Assert.That(effect.whistle, Is.Not.Null, "Свист в полёте.");
            Assert.That(effect.catchSound, Is.Not.Null);
        }

        [Test]
        public void FlightTurnsAtItsRangeAndComesBackToThePlayer([Values(1, 2, 3)] int level)
        {
            var effect = Effect();
            var path = Throw(effect, level, new Vector3(0, 0, 14));
            bool caught = Fly(effect, ref path, new Vector3(0, -1.1f, 0), out float farthest, out float leftmost);
            float range = effect.range.At(level);
            Assert.That(caught, Is.True, "Вернулся и пойман.");
            Assert.That(farthest, Is.EqualTo(range).Within(.75f), "Разворот на дальности уровня.");
            Assert.That(leftmost, Is.LessThan(-1), "Дуга уходит влево, как у бумеранга.");
            Assert.That(path.Elapsed, Is.LessThan(path.Lifetime));
            Assert.That(path.Returning, Is.True);
        }

        [Test]
        public void PreviewShowsTheWholeTurnButEndsTheReturnAFewMetresAway([Values(1, 2, 3)] int level)
        {
            var effect = Effect();
            Assert.That(effect.previewReturnStop, Is.InRange(3f, 4f), "Обратный путь в превью — до ~3–4 м от игрока.");
            var path = Throw(effect, level, new Vector3(0, 0, 14));
            Vector3 chest = new Vector3(0, -.2f, 0);
            float farthest = 0;
            Vector3 end = default;
            bool ended = false;
            // Шаги, как у превью (128 точек на весь срок полёта).
            float step = path.Lifetime / 127;
            for (int i = 0; i < 127 && !ended; i++)
            {
                Vector3 from = path.Position;
                BoomerangRules.Advance(ref path, chest, step);
                farthest = Mathf.Max(farthest, Vector3.Dot(path.Position - path.Origin, path.Forward));
                ended = BoomerangRules.PreviewReturnEnds(path, from, chest, effect.previewReturnStop, out end);
            }
            Assert.That(ended, Is.True, "Превью кончается на обратном пути, а не у груди.");
            Assert.That(farthest, Is.EqualTo(effect.range.At(level)).Within(.75f), "Путь туда и разворот — целиком.");
            end.y = 0;
            Assert.That(end.magnitude, Is.EqualTo(effect.previewReturnStop).Within(.01f), "Конец — ровно на границе.");
            // На пути туда граница не срабатывает, хотя карта начинает у самого игрока.
            var start = Throw(effect, level, new Vector3(0, 0, 14));
            Vector3 was = start.Position;
            BoomerangRules.Advance(ref start, chest, step);
            Assert.That(BoomerangRules.PreviewReturnEnds(start, was, chest, effect.previewReturnStop, out _), Is.False);
        }

        [Test]
        public void TheSamePathWhateverTheStep()
        {
            var effect = Effect();
            var a = Throw(effect, 2, new Vector3(3, 2, 12));
            var b = a;
            Vector3 target = new Vector3(0, 1, 0);
            BoomerangRules.Advance(ref a, target, 1.6f);
            for (int i = 0; i < 80; i++) BoomerangRules.Advance(ref b, target, .02f);
            Assert.That(Vector3.Distance(a.Position, b.Position), Is.LessThan(.02f), "Превью и полёт совпадают.");
        }

        [Test]
        public void CatchesAPlayerWhoMovedAndDoesNotCircle()
        {
            var effect = Effect();
            var path = Throw(effect, 3, new Vector3(0, 0, 18));
            // Игрок отбежал на 4 м вбок и назад: бумеранг всё равно доходит до него, а не кружит.
            Assert.That(Fly(effect, ref path, new Vector3(4, -1.1f, -3), out _, out _), Is.True);
        }

        [Test]
        public void WithoutACatchItDropsWhenTimeIsUp()
        {
            var effect = Effect();
            var path = Throw(effect, 1, new Vector3(0, 0, 11));
            // Игрок убежал далеко назад: не долетела — время кончилось.
            Assert.That(Fly(effect, ref path, new Vector3(0, -1.1f, -60), out _, out _), Is.False);
            Assert.That(BoomerangRules.TimedOut(path), Is.True);
        }

        [Test]
        public void SpeedAndPitchStayWithinLimits()
        {
            var effect = Effect();
            var slow = Throw(effect, 1, new Vector3(0, 0, 3));
            Assert.That(slow.Speed, Is.EqualTo(effect.minimumSpeed));
            var steep = Throw(effect, 1, new Vector3(0, 30, 2));
            float pitch = Mathf.Asin(steep.Velocity.normalized.y) * Mathf.Rad2Deg;
            Assert.That(pitch, Is.EqualTo(effect.maximumPitch).Within(.1f));
            Assert.That(steep.Speed, Is.EqualTo(effect.maximumSpeed));
        }

        [Test]
        public void OneHitPerCreatureOnEachLeg()
        {
            var hits = new BoomerangHits();
            object dog = new object(), toad = new object();
            Assert.That(hits.TryHit(dog, false), Is.True, "Туда — первый раз ранит.");
            Assert.That(hits.TryHit(dog, false), Is.False, "Туда — второй раз нет.");
            Assert.That(hits.TryHit(toad, false), Is.True, "Другой зверь в ряду — тоже.");
            Assert.That(hits.TryHit(dog, true), Is.True, "Обратно — ещё раз.");
            Assert.That(hits.TryHit(dog, true), Is.False);
            Assert.That(hits.TryHit(null, true), Is.False);
        }

        [Test]
        public void PushGoesAlongTheFlightAndALittleUp()
        {
            var push = BoomerangRules.Push(new Vector3(0, -5, 10), 2, .25f);
            Assert.That(Vector3.Distance(push, new Vector3(0, .5f, 2)), Is.LessThan(1e-4f));
        }

        static Expedition TripWith(CardSpec spec, out CardCopy card)
        {
            var trip = new Expedition(new CardCopy[0], 3, 1);
            card = trip.GrantToHand(spec);
            return trip;
        }

        [Test]
        public void CaughtTheSameCopyIsBackInTheHand()
        {
            var trip = TripWith(Card().ToSpec(), out var card);
            var other = trip.GrantToHand(new CardSpec("strike", "Удар", 50));
            Assert.That(card.UsesLeft, Is.EqualTo(1));
            Assert.That(trip.Release(card.Id, true), Is.SameAs(card));
            Assert.That(trip.Deck.Hand.Contains(card), Is.False, "В полёте карты нет в руке…");
            Assert.That(trip.Deck.Held.Single(), Is.SameAs(card), "…но место за ней.");
            Assert.That(trip.Catch(card.Id), Is.True);
            Assert.That(trip.Deck.Hand.First(), Is.SameAs(card), "Та же копия, на своём месте.");
            Assert.That(trip.Deck.Hand.Last(), Is.SameAs(other));
            Assert.That(card.UsesLeft, Is.EqualTo(1), "Использование возвращено: можно бросить снова.");
            Assert.That(trip.Deck.Held, Is.Empty);
            Assert.That(trip.Deck.Discard, Is.Empty);
            Assert.That(trip.Settle(card.Id), Is.False, "Конец попытки после поимки ничего не меняет.");
            Assert.That(trip.Deck.Hand.Contains(card), Is.True);
        }

        [Test]
        public void NotCaughtItGoesToTheSpentPile()
        {
            var trip = TripWith(Card().ToSpec(), out var card);
            trip.Release(card.Id, true);
            Assert.That(trip.Settle(card.Id), Is.False, "Попытка кончилась без поимки…");
            Assert.That(trip.Deck.Discard.Single(), Is.SameAs(card), "…карта в сбросе.");
            Assert.That(trip.Deck.Held, Is.Empty);
            Assert.That(trip.Catch(card.Id), Is.False, "Поймать после падения нельзя.");
            Assert.That(trip.Deck.HasReturn, Is.True, "Рука пуста — появилась карта возвращения.");
        }

        [Test]
        public void AnOrdinaryCardIsNotHeld()
        {
            var trip = TripWith(new CardSpec("strike", "Удар", 50), out var card);
            trip.Release(card.Id);
            Assert.That(trip.Deck.Held, Is.Empty);
            Assert.That(trip.Deck.Discard.Single(), Is.SameAs(card));
            Assert.That(trip.Catch(card.Id), Is.False);
        }

        [Test]
        public void AWallStopsItButACreatureDoesNot()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(wall);
            wall.transform.position = new Vector3(0, 1, 8);
            wall.transform.localScale = new Vector3(4, 4, .5f);
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/Goblin dog.prefab");
            Assert.That(prefab, Is.Not.Null);
            var dog = Object.Instantiate(prefab, new Vector3(0, 0, 4), Quaternion.identity);
            spawned.Add(dog.gameObject);
            Physics.SyncTransforms();
            var body = dog.GetComponentsInChildren<Collider>().First(c => !c.isTrigger && c.enabled);
            Vector3 through = body.bounds.center;
            Vector3 from = new Vector3(0, through.y, 0), to = new Vector3(0, through.y, 12);
            Assert.That(BoomerangFlight.SweepSolid(from, to, .06f, out var hit), Is.True, "Стена — препятствие.");
            Assert.That(hit.collider.gameObject, Is.SameAs(wall), "Сквозь зверя пролетает, о стену ударяется.");
            Assert.That(Physics.SphereCast(from, .06f, Vector3.forward, out var first, 12), Is.True);
            Assert.That(Combat.Of(first.collider), Is.SameAs(dog), "Зверь действительно на пути, раньше стены.");
            Assert.That(BoomerangFlight.SweepSolid(from, new Vector3(0, through.y, 6), .06f, out _), Is.False);
        }

        [Test]
        public void EchoRepeatsItAsAnInstantHit()
        {
            Assert.That(EchoRules.CanRepeat(Card(), false, new[] { "teleport", "echo", "return" }), Is.True);
        }
    }
}
