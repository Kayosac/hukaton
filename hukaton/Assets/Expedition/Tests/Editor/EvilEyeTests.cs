using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Правила «Сглаза»: числа таблицы, задержка, смерть до срока отменяет метку, две метки срабатывают обе.</summary>
    public sealed class EvilEyeTests
    {
        /// <summary>Существо карт без сцены: записывает полученные удары.</summary>
        sealed class Dummy : ICardCreature
        {
            public readonly List<DamageInfo> Hits = new List<DamageInfo>();
            public readonly Transform root;
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

        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var o in spawned) if (o) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        static CardDefinition Card()
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            var card = config.Find("evil_eye");
            Assert.That(card, Is.Not.Null, "Сглаз есть в каталоге.");
            return card;
        }

        static EvilEyeEffect Effect()
        {
            var effect = Card().effectAsset as EvilEyeEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        Dummy NewDummy()
        {
            var go = new GameObject("Dummy beast");
            spawned.Add(go);
            return new Dummy(go.transform);
        }

        EvilEyeMark NewMark(Dummy target, int damage, float popAt, float now)
        {
            var mark = (EvilEyeMark)PrefabUtility.InstantiatePrefab(Effect().markPrefab);
            spawned.Add(mark.gameObject);
            mark.Attach(target, damage, popAt, now);
            return mark;
        }

        [Test]
        public void CardHasTheTablesRankPriceAndUses()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.EvilEye));
            Assert.That((int)CardAction.EvilEye, Is.EqualTo(38));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((4, 270)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
            Assert.That(card.description, Is.EqualTo("Чёрная метка на звере лопается с задержкой и сильно ранит."));
        }

        [Test]
        public void DamageGrowsWithTheLevelAndTheDelayIsTwoSeconds()
        {
            var effect = Effect();
            Assert.That((effect.damage.At(1), effect.damage.At(2), effect.damage.At(3)), Is.EqualTo((6, 9, 14)));
            Assert.That(effect.delay, Is.EqualTo(2));
            Assert.That(effect.TryGetStat(out var label, out var values, out _), Is.True);
            Assert.That(label, Is.EqualTo("Урон"));
            Assert.That((values.i, values.ii, values.iii), Is.EqualTo((6f, 9f, 14f)), "ПК показывает урон.");
        }

        [Test]
        public void MarkWaitsThenPopsForItsDamage()
        {
            var beast = NewDummy();
            var mark = NewMark(beast, 9, 2, 0);
            Assert.That(mark.Tick(1.99f), Is.EqualTo(EvilEyeRules.Step.Wait), "До срока — ждёт.");
            Assert.That(beast.Hits, Is.Empty);
            Assert.That(mark.Tick(2f), Is.EqualTo(EvilEyeRules.Step.Pop));
            Assert.That(beast.Hits.Count, Is.EqualTo(1));
            Assert.That(beast.Hits[0].Amount, Is.EqualTo(9));
            Assert.That(beast.Hits[0].Kind, Is.EqualTo(DamageKind.Other), "Урон тьмой, а не физический удар (не колет лёд).");
            Assert.That(mark == null, Is.True, "Лопнувшая метка удаляется.");
        }

        [Test]
        public void DeathBeforeTheDeadlineCancelsTheMark()
        {
            var beast = NewDummy();
            var mark = NewMark(beast, 14, 2, 0);
            beast.health = 0;
            Assert.That(mark.Tick(.5f), Is.EqualTo(EvilEyeRules.Step.Vanish));
            Assert.That(beast.Hits, Is.Empty, "Мёртвому — никакого урона.");
            Assert.That(mark == null, Is.True, "Метка пропала.");
        }

        [Test]
        public void UnloadedBeastTakesTheMarkAway()
        {
            var beast = NewDummy();
            var mark = NewMark(beast, 6, 2, 0);
            Object.DestroyImmediate(beast.root.gameObject);
            Assert.That(mark.Tick(2.5f), Is.EqualTo(EvilEyeRules.Step.Vanish), "Зверь выгрузился с чанком — без ошибок и урона.");
            Assert.That(beast.Hits, Is.Empty);
        }

        [Test]
        public void TwoMarksBothPopEachOnItsOwnTimer()
        {
            var beast = NewDummy();
            var first = NewMark(beast, 6, 2, 0);
            var second = NewMark(beast, 9, 2.7f, .7f);
            Assert.That(EvilEyeMark.CountOn(beast), Is.EqualTo(2));
            first.Tick(2);
            Assert.That(beast.Hits.Count, Is.EqualTo(1));
            Assert.That(second.Tick(2), Is.EqualTo(EvilEyeRules.Step.Wait), "Вторая метка ещё ждёт свой срок.");
            Assert.That(second.Tick(2.7f), Is.EqualTo(EvilEyeRules.Step.Pop));
            Assert.That(beast.Hits.ConvertAll(h => h.Amount), Is.EqualTo(new[] { 6, 9 }));
            Assert.That(EvilEyeMark.CountOn(beast), Is.EqualTo(0));
        }

        [Test]
        public void OnlyLivingBeastsAndCardCreaturesCanBeMarked()
        {
            var beast = NewDummy();
            Assert.That(EvilEyeRules.CanMark(beast, null), Is.True, "Мимик и любое существо карт.");
            Assert.That(EvilEyeRules.CanMark(beast, beast), Is.False, "Не игрок.");
            Assert.That(EvilEyeRules.CanMark(null, null), Is.False, "Промах — пшик.");
            beast.health = 0;
            Assert.That(EvilEyeRules.CanMark(beast, null), Is.False, "Мёртвого не помечаем.");
        }

        [Test]
        public void SignSwellsAndQuickensTowardThePop()
        {
            Assert.That(EvilEyeRules.Progress(0, 0, 2), Is.EqualTo(0));
            Assert.That(EvilEyeRules.Progress(1, 0, 2), Is.EqualTo(.5f));
            Assert.That(EvilEyeRules.Progress(3, 0, 2), Is.EqualTo(1));
            Assert.That(EvilEyeRules.Swell(1, .75f, 1.35f), Is.GreaterThan(EvilEyeRules.Swell(.5f, .75f, 1.35f)));
            Assert.That(EvilEyeRules.PulseRate(1, 1.5f, 7), Is.EqualTo(7));
            var feet = new Vector3(1, 0, 2);
            var sign = EvilEyeRules.SignPosition(feet, feet + Vector3.up * .5f, .3f, 1, .5f);
            Assert.That(Vector3.Distance(sign, feet + Vector3.up * 1.8f), Is.LessThan(1e-4f), "Над макушкой, вторая метка выше.");
        }

        [Test]
        public void MarkPrefabIsReadyAndLeavesNothingSolid()
        {
            var prefab = Effect().markPrefab;
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.sign, Is.Not.Null);
            Assert.That(prefab.loops, Is.Not.Empty);
            foreach (var loop in prefab.loops) Assert.That(loop.main.loop, Is.True, "Знак виден всё время отсчёта.");
            Assert.That(prefab.popVfx, Is.Not.Null);
            Assert.That(prefab.popSound, Is.Not.Null);
            Assert.That(Effect().missSound, Is.Not.Null, "Промах — пшик.");
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (var source in prefab.GetComponentsInChildren<AudioSource>(true))
                Assert.That(source.enabled && source.playOnAwake, Is.False);
        }
    }
}
