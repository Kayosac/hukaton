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
    /// Правила «Веера»: числа таблицы; 3 / 5 / 7 карт равномерно в 40° по горизонтали, средняя — ровно по прицелу;
    /// каждая ранит на 1 / 1 / 2 (мёртвому — ничего); в упор зверь получает всю пачку, издали — одну карту; тратится одна
    /// копия, её попытка кончается ровно раз — после последней карты веера; кривые руки отклоняют весь веер; карты друг
    /// друга не сбивают; Эхо бьёт всеми картами веера разом.
    /// </summary>
    public sealed class FanTests
    {
        /// <summary>Существо карт без сцены: записывает полученные удары.</summary>
        sealed class Dummy : ICardCreature
        {
            public readonly List<DamageInfo> Hits = new List<DamageInfo>();
            readonly Transform root;
            public int health = 100;
            public Dummy(Transform root) { this.root = root; }
            public Faction Faction => Faction.Wild;
            public bool Alive => health > 0;
            public Vector3 Position => root.position;
            public Vector3 Centre => root.position + Vector3.up * .6f;
            public Transform Root => root;
            public bool TakeDamage(in DamageInfo damage)
            {
                if (!Alive) return false;
                Hits.Add(damage);
                health -= damage.Amount;
                return true;
            }
            public void Freeze(float seconds) { }
            public void Thaw() { }
        }

        // Далеко от геометрии открытой сцены: физические проверки видят только своё.
        static readonly Vector3 Far = new Vector3(4000, 300, 4000);
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
            var card = config.Find("fan");
            Assert.That(card, Is.Not.Null, "Веер есть в каталоге.");
            return card;
        }

        static FanEffect Effect()
        {
            var effect = Card().effectAsset as FanEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        static CardFlightLaunch Launch(Vector3 velocity, int level, Vector3 origin = default)
        {
            var copy = new Expedition(new CardCopy[0], 3, 1).GrantToHand(Card().ToSpec(), level);
            Assert.That(copy.Level, Is.EqualTo(level));
            return new CardFlightLaunch(null, copy, Card(), origin, velocity);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        Dummy NewDummy(int health = 100)
        {
            var go = new GameObject("Dummy beast");
            spawned.Add(go);
            return new Dummy(go.transform) { health = health };
        }

        [Test]
        public void CardHasTheTablesNumbers()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.Fan));
            Assert.That((int)CardAction.Fan, Is.EqualTo(42));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((1, 150)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
            Assert.That(card.description, Is.EqualTo("Бросает сразу несколько карт веером. Каждая ранит."));
            Assert.That(card.accent.r > .85f && card.accent.g < .3f && card.accent.b < .3f, Is.True, "Контрастный красный Боя.");
            var effect = Effect();
            Assert.That((effect.cards.i, effect.cards.ii, effect.cards.iii), Is.EqualTo((3, 5, 7)));
            Assert.That((effect.damage.i, effect.damage.ii, effect.damage.iii), Is.EqualTo((1, 1, 2)));
            Assert.That(effect.spreadDegrees, Is.EqualTo(40));
            Assert.That(effect.TryGetStat(out var label, out var values, out var unit), Is.True);
            Assert.That((label, values.i, values.ii, values.iii, unit), Is.EqualTo(("Карт в веере", 3f, 5f, 7f, "")), "ПК показывает число карт.");
            Assert.That(effect is ICardVolley, Is.True, "Один бросок — несколько карт.");
            Assert.That((ScriptableObject)effect is ICustomFlight, Is.False, "Каждая карта летит обычной баллистикой.");
            Assert.That(effect.impactVfx, Is.Not.Null, "Искры попаданий.");
            Assert.That(effect.layerSound, Is.Not.Null, "Наложенные взмахи броска.");
        }

        [Test]
        public void CardsAndAnglesGrowWithTheLevel()
        {
            var effect = Effect();
            var aim = new Vector3(3, 2, 12);
            var velocities = new Vector3[16];
            var rolls = new float[16];
            int[] expected = { 3, 5, 7 };
            for (int level = 1; level <= 3; level++)
            {
                int count = effect.Volley(Launch(aim, level), velocities, rolls, out int centre);
                Assert.That(count, Is.EqualTo(expected[level - 1]), "Карт на уровне " + level);
                Assert.That(centre, Is.EqualTo(count / 2));
                Assert.That(Vector3.Distance(velocities[centre], aim), Is.LessThan(1e-4f), "Средняя — ровно по прицелу.");
                float step = 40f / (count - 1);
                for (int i = 0; i < count; i++)
                {
                    var v = velocities[i];
                    Assert.That(v.magnitude, Is.EqualTo(aim.magnitude).Within(1e-3f), "Скорость у всех та же.");
                    Assert.That(v.y, Is.EqualTo(aim.y).Within(1e-4f), "Веер по горизонтали: наклон тот же.");
                    float angle = Vector3.SignedAngle(Flat(aim), Flat(v), Vector3.up);
                    Assert.That(angle, Is.EqualTo(-20 + step * i).Within(.01f), "Равномерно в 40°, крайние — по 20° в стороны.");
                }
                Assert.That(rolls[0], Is.EqualTo(effect.handSpreadDegrees).Within(1e-3f), "Левая карта в руке клонится влево…");
                Assert.That(rolls[count - 1], Is.EqualTo(-effect.handSpreadDegrees).Within(1e-3f), "…правая — вправо.");
                Assert.That(rolls[centre], Is.EqualTo(0).Within(1e-4f));
            }
        }

        [Test]
        public void EachCardWoundsForItsDamage()
        {
            var effect = Effect();
            int[] expected = { 1, 1, 2 };
            for (int level = 1; level <= 3; level++)
            {
                var beast = NewDummy();
                Assert.That(effect.HitsPerImpact(false, level), Is.EqualTo(1), "Попадание одной карты — один удар.");
                Assert.That(FanRules.Wound(null, beast, effect.damage.At(level), 1, Vector3.forward * effect.nudge, Far, Far + Vector3.back), Is.True);
                Assert.That(beast.Hits.Single().Amount, Is.EqualTo(expected[level - 1]), "Урон карты на уровне " + level);
                Assert.That(beast.Hits[0].Kind, Is.EqualTo(DamageKind.Hit), "Физический удар, как у Удара.");
                Assert.That(beast.Hits[0].Impulse.magnitude, Is.EqualTo(effect.nudge).Within(1e-4f), "Небольшой толчок.");
            }
            var dead = NewDummy(0);
            Assert.That(FanRules.Wound(null, dead, 2, 1, Vector3.zero, Far, Far), Is.False, "Мёртвому — ничего.");
            Assert.That(dead.Hits, Is.Empty);
            var weak = NewDummy(3);
            FanRules.Wound(null, weak, 1, 7, Vector3.zero, Far, Far);
            Assert.That(weak.Hits.Count, Is.EqualTo(3), "Пачка в упор: после смерти удары не идут.");
            Assert.That(FanRules.Wound(null, null, 2, 1, Vector3.zero, Far, Far), Is.False, "Земля, стена — никого не ранит.");
        }

        /// <summary>Летит обычной баллистикой до первого касания; true — коснулась <paramref name="target"/>.</summary>
        static bool Hits(Vector3 from, Vector3 velocity, ExpeditionSettings settings, Object target)
        {
            Vector3 position = from;
            for (int i = 0; i < 100; i++)
            {
                Vector3 next = position, nextVelocity = velocity;
                CardFlight.Advance(ref next, ref nextVelocity, settings, 1f / 50f);
                if (CardFlight.Sweep(position, next, settings.cardRadius, out var hit)) return ReferenceEquals(Combat.Of(hit.collider), target);
                position = next;
                velocity = nextVelocity;
            }
            return false;
        }

        [Test]
        public void PointBlankABeastTakesTheWholePackFromAfarOneCard()
        {
            var effect = Effect();
            var settings = AssetDatabase.LoadAssetAtPath<ExpeditionSettings>("Assets/Game/Data/Expedition.asset");
            Assert.That(settings, Is.Not.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/Goblin dog.prefab");
            Assert.That(prefab, Is.Not.Null);
            var velocities = new Vector3[16];
            foreach (var (distance, level, expected) in new[] { (1f, 3, 7), (1f, 1, 3), (5f, 3, 1) })
            {
                var dog = Object.Instantiate(prefab, Far + new Vector3(0, 0, distance), Quaternion.identity);
                spawned.Add(dog.gameObject);
                Physics.SyncTransforms();
                var body = dog.GetComponentsInChildren<Collider>().First(c => !c.isTrigger && c.enabled);
                Vector3 from = new Vector3(Far.x, body.bounds.center.y + .2f, Far.z);
                float speed = 14, time = distance / speed;
                // Прицел на грудь зверя с поправкой на падение карты.
                var aim = new Vector3(0, -.2f / time + .5f * settings.cardGravity * time, speed);
                int count = effect.Volley(Launch(aim, level, from), velocities, null, out _);
                int hits = Enumerable.Range(0, count).Count(i => Hits(from, velocities[i], settings, dog));
                Assert.That(hits, Is.EqualTo(expected), $"{distance} м, уровень {level}: попало карт.");
                Object.DestroyImmediate(dog.gameObject);
            }
        }

        [Test]
        public void OneCopyIsSpentAndSettledExactlyOnce()
        {
            var trip = new Expedition(new CardCopy[0], 3, 1);
            var card = trip.GrantToHand(Card().ToSpec(), 3);
            Assert.That(trip.Release(card.Id), Is.SameAs(card), "Бросок веера — один выпуск одной копии.");
            Assert.That(trip.PlayedCount, Is.EqualTo(1));
            Assert.That(card.UsesLeft, Is.Zero, "Потрачено одно использование.");
            Assert.That(trip.Deck.Discard.Single(), Is.SameAs(card), "Как обычная карта — сразу в сброс.");
            var volley = new CardVolley(7);
            var thrown = Enumerable.Range(0, 7).Select(_ => new object()).ToArray();
            int settled = 0;
            foreach (var one in thrown)
                if (volley.Finish(one)) { settled++; trip.Settle(card.Id); }
            Assert.That(settled, Is.EqualTo(1), "Попытка кончается один раз — после последней карты.");
            Assert.That(volley.Settled, Is.True);
            Assert.That(volley.Finish(thrown[0]), Is.False);
            Assert.That(trip.Deck.Discard.Count(c => ReferenceEquals(c, card)), Is.EqualTo(1), "В сбросе ровно одна копия.");
            Assert.That(trip.Deck.Hand.Contains(card), Is.False);
            Assert.That(trip.Deck.Held, Is.Empty);
            Assert.That(trip.Deck.HasReturn, Is.True, "Рука пуста — появилась карта возвращения.");
        }

        [Test]
        public void ACopyWithUsesLeftComesBackOnlyAfterTheLastCardOfTheFan()
        {
            var trip = new Expedition(new CardCopy[0], 3, 1);
            var card = trip.GrantToHand(Card().ToSpec(), 1, 2);
            trip.Release(card.Id);
            Assert.That(trip.Deck.Held.Single(), Is.SameAs(card));
            var volley = new CardVolley(3);
            object a = new object(), b = new object(), c = new object();
            Assert.That(volley.Finish(a), Is.False);
            Assert.That(volley.Finish(a), Is.False, "Одна и та же карта дважды не считается.");
            Assert.That(volley.Finish(b), Is.False);
            Assert.That(trip.Deck.Held.Single(), Is.SameAs(card), "Пока летит хоть одна карта веера, копия в воздухе.");
            Assert.That(volley.Finish(c), Is.True);
            Assert.That(trip.Settle(card.Id), Is.True, "После последней — назад в руку.");
            Assert.That(trip.Deck.Hand.Contains(card), Is.True);
            Assert.That(card.UsesLeft, Is.EqualTo(1));
            Assert.That(trip.PlayedCount, Is.EqualTo(1));
        }

        [Test]
        public void CrookedHandsBendTheWholeFan()
        {
            var effect = Effect();
            var aim = new Vector3(0, 1.5f, 14);
            var straight = new Vector3[16];
            var crooked = new Vector3[16];
            int count = effect.Volley(Launch(aim, 3), straight, null, out int centre);
            // Как в CardThrower.Release: сначала кривые руки гнут бросок, потом от него раскрывается веер.
            var bent = PlayerStatusRules.Crook(aim, Vector3.up, Vector3.right, 12, 1, .5f);
            Assert.That(effect.Volley(Launch(bent, 3), crooked, null, out _), Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.That(Vector3.SignedAngle(Flat(straight[i]), Flat(crooked[i]), Vector3.up), Is.EqualTo(12).Within(.05f),
                    "Каждая карта отклонена в ту же сторону на тот же угол.");
                Assert.That(crooked[i].y, Is.EqualTo(straight[i].y).Within(1e-3f));
            }
            Assert.That(Vector3.Angle(Flat(crooked[0]), Flat(crooked[count - 1])), Is.EqualTo(40).Within(.05f), "Веер не сломан.");
            Assert.That(Vector3.Distance(crooked[centre], bent), Is.LessThan(1e-4f));
            // Вверх-вниз — тоже весь веер.
            var dipped = PlayerStatusRules.Crook(aim, Vector3.up, Vector3.right, 12, .5f, 1);
            effect.Volley(Launch(dipped, 3), crooked, null, out _);
            for (int i = 0; i < count; i++)
                Assert.That(crooked[i].y, Is.LessThan(straight[i].y - .5f), "Каждая карта ушла вниз.");
        }

        [Test]
        public void CardsOfAFanNeverStrikeEachOther()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<CardProjectile>("Assets/Game/Prefabs/Cards/Thrown card.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, "У летящей карты нет коллайдера: соседке не во что попасть.");
            int cards = LayerMask.NameToLayer("Cards");
            if (cards >= 0) Assert.That(CardFlight.CollisionMask & (1 << cards), Is.Zero, "Слой карт вне маски столкновений карт.");
        }

        [Test]
        public void EchoStrikesWithTheWholeFanAtItsPoint()
        {
            Assert.That(EchoRules.CanRepeat(Card(), false, new[] { "teleport", "echo", "return" }), Is.True);
            Assert.That(FanRules.IsEcho("echo", "fan"), Is.True, "Эхо передаёт свою копию с определением Веера.");
            Assert.That(FanRules.IsEcho("fan", "fan"), Is.False);
            var effect = Effect();
            int[] totals = { 3, 5, 14 };
            for (int level = 1; level <= 3; level++)
            {
                var beast = NewDummy();
                int hits = effect.HitsPerImpact(true, level);
                Assert.That(hits, Is.EqualTo(effect.cards.At(level)), "Все карты веера уровня Эха.");
                FanRules.Wound(null, beast, effect.damage.At(level), hits, Vector3.zero, Far, Far);
                Assert.That(beast.Hits.Sum(h => h.Amount), Is.EqualTo(totals[level - 1]), "Как веер в упор, уровень " + level);
            }
        }
    }
}
