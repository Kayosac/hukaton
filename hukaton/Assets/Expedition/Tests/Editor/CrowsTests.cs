using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Правила «Вороньё»: числа таблицы, урон клевков по уровням и времени, отвлечение включается и снимается,
    /// смерть зверя разгоняет ворон, повторное попадание продлевает стаю, союзников и игрока не клюют.
    /// </summary>
    public sealed class CrowsTests
    {
        /// <summary>Существо карт без сцены: записывает удары и отвлечение.</summary>
        sealed class Dummy : ICardCreature, IDistractable
        {
            public readonly List<DamageInfo> Hits = new List<DamageInfo>();
            public readonly Transform root;
            public int health = 100;
            public Faction faction = Faction.Wild;
            public float distractedFor;
            public bool distracted;
            public int ended;
            public Dummy(Transform root) { this.root = root; }
            public Faction Faction => faction;
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
            public bool Distract(float seconds) { distracted = true; distractedFor = seconds; return true; }
            public void EndDistraction() { distracted = false; ended++; }
            public bool Distracted => distracted;
            public int Taken { get { int sum = 0; foreach (var h in Hits) sum += h.Amount; return sum; } }
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
            var card = config.Find("crows");
            Assert.That(card, Is.Not.Null, "Вороньё есть в каталоге.");
            return card;
        }

        static CrowsEffect Effect()
        {
            var effect = Card().effectAsset as CrowsEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        Dummy NewDummy()
        {
            var go = new GameObject("Dummy beast");
            spawned.Add(go);
            return new Dummy(go.transform);
        }

        CrowFlock NewFlock()
        {
            var flock = (CrowFlock)PrefabUtility.InstantiatePrefab(Effect().flockPrefab);
            spawned.Add(flock.gameObject);
            return flock;
        }

        /// <summary>Шаги по 0,1 с от <paramref name="from"/> до <paramref name="to"/> включительно.</summary>
        static void Run(CrowFlock flock, float from, float to)
        {
            for (float t = from; t <= to + 1e-4f; t += .1f)
                if (flock) flock.Tick(t);
        }

        [Test]
        public void CardHasTheTablesRankPriceUsesAndLook()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.Crows));
            Assert.That((int)CardAction.Crows, Is.EqualTo(39));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((3, 200)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
            Assert.That(card.description, Is.EqualTo("Вороны клюют зверя. Пока отбивается, он не нападает."));
            Assert.That(ColorUtility.ToHtmlStringRGB(card.accent), Is.EqualTo("A64B50"));
        }

        [Test]
        public void DamagePerSecondGrowsWithTheLevelForFourSeconds()
        {
            var effect = Effect();
            Assert.That((effect.damagePerSecond.At(1), effect.damagePerSecond.At(2), effect.damagePerSecond.At(3)), Is.EqualTo((1, 2, 3)));
            Assert.That(effect.duration, Is.EqualTo(4));
            Assert.That(effect.TryGetStat(out var label, out var values, out _), Is.True);
            Assert.That(label, Is.EqualTo("Урон/с"));
            Assert.That((values.i, values.ii, values.iii), Is.EqualTo((1f, 2f, 3f)), "ПК показывает урон в секунду.");
        }

        [TestCase(1, 4)]
        [TestCase(2, 8)]
        [TestCase(3, 12)]
        public void PecksHurtEverySecondForTheTablesTotal(int perSecond, int total)
        {
            var beast = NewDummy();
            var flock = NewFlock();
            flock.Attach(beast, perSecond, 4, 0);
            Run(flock, 0, .9f);
            Assert.That(beast.Hits, Is.Empty, "Первый ранящий клевок — через секунду.");
            Run(flock, 1, 2.05f);
            Assert.That(beast.Taken, Is.EqualTo(perSecond * 2), "Через 2 с — два клевка.");
            Run(flock, 2.1f, 6);
            Assert.That(beast.Hits.Count, Is.EqualTo(4), "Четыре клевка за 4 с.");
            Assert.That(beast.Taken, Is.EqualTo(total));
            foreach (var hit in beast.Hits) Assert.That(hit.Kind, Is.EqualTo(DamageKind.Hit));
        }

        [Test]
        public void APeckDealsDamagePerSecondTimesTheIntervalWithoutLosingAny()
        {
            float owed = 0;
            var pecks = new List<int>();
            for (int i = 0; i < 8; i++) pecks.Add(CrowsRules.PeckDamage(ref owed, 3, .5f));
            Assert.That(pecks, Is.EqualTo(new[] { 1, 2, 1, 2, 1, 2, 1, 2 }), "3 HP/с раз в 0,5 с: остаток копится");
            owed = 0;
            Assert.That(CrowsRules.PeckDamage(ref owed, 2, 1), Is.EqualTo(2), "раз в секунду — урон в секунду");
            owed = 0;
            Assert.That(CrowsRules.PeckDamage(ref owed, 1, 2), Is.EqualTo(2), "раз в 2 с — вдвое больше");

            // Стая с клевком раз в 0,5 с наносит за 4 с ровно 3 × 4 = 12.
            var beast = NewDummy();
            var flock = NewFlock();
            flock.peckInterval = .5f;
            flock.Attach(beast, 3, 4, 0);
            Run(flock, 0, 4.2f);
            Assert.That(beast.Hits.Count, Is.EqualTo(8));
            Assert.That(beast.Taken, Is.EqualTo(12));
        }

        [Test]
        public void TheFlockFliesAlongWithTheBeastAndAHiddenOneIsGone()
        {
            var beast = NewDummy();
            var flock = NewFlock();
            flock.Attach(beast, 1, 4, 0);
            beast.root.position = new Vector3(30, 0, -12);
            flock.Tick(.1f);
            var head = CrowsRules.HeadTop(beast.Position, beast.Centre, flock.headGap);
            Assert.That(Vector3.Distance(flock.transform.position, head), Is.LessThan(1e-3f),
                "корень стаи — над зверем: выгружается вместе с его чанком, а не там, куда попала карта");
            Assert.That(CrowFlock.On(beast), Is.SameAs(flock));
            flock.gameObject.SetActive(false);
            Assert.That(CrowFlock.On(beast), Is.Null, "спрятанную стаю новое попадание не продлит — позовёт новых ворон");
        }

        [Test]
        public void BeastIsDistractedWhilePeckedAndReleasedWhenTheCrowsLeave()
        {
            var beast = NewDummy();
            var flock = NewFlock();
            flock.Attach(beast, 2, 4, 0);
            Assert.That(beast.Distracted, Is.True, "Вороны сели — зверь отвлечён сразу.");
            Assert.That(CrowFlock.On(beast), Is.SameAs(flock));
            Run(flock, 0, 3.9f);
            Assert.That(beast.Distracted, Is.True, "Весь срок отвлечён.");
            Assert.That(beast.distractedFor, Is.GreaterThan(0).And.LessThanOrEqualTo(4));
            Assert.That(flock.Tick(4), Is.EqualTo(CrowsRules.Step.Leave), "Срок вышел — улетают.");
            Assert.That(beast.Distracted, Is.False, "Отвлечение снято: зверь снова может нападать.");
            Assert.That(beast.ended, Is.EqualTo(1));
            Assert.That(CrowFlock.On(beast), Is.Null);
            Assert.That(flock.Tick(4 + flock.leaveSeconds), Is.EqualTo(CrowsRules.Step.Done));
            Assert.That(flock == null, Is.True, "Улетевшая стая удаляется.");
        }

        [Test]
        public void DeadBeastScattersTheCrows()
        {
            var beast = NewDummy();
            var flock = NewFlock();
            flock.Attach(beast, 3, 4, 0);
            Run(flock, 0, 1.5f);
            Assert.That(beast.Hits.Count, Is.EqualTo(1));
            beast.health = 0;
            Assert.That(flock.Tick(1.6f), Is.EqualTo(CrowsRules.Step.Leave), "Зверь умер — вороны разлетаются.");
            Assert.That(flock.Leaving, Is.True);
            Run(flock, 1.7f, 3);
            Assert.That(beast.Hits.Count, Is.EqualTo(1), "Мёртвого больше не клюют.");
            Assert.That(beast.ended, Is.Zero, "Снимать отвлечение с мёртвого незачем.");
        }

        [Test]
        public void KillingPeckEndsTheFlock()
        {
            var beast = NewDummy();
            beast.health = 5;
            var flock = NewFlock();
            flock.Attach(beast, 3, 4, 0);
            Run(flock, 0, 2.05f);
            Assert.That(beast.Alive, Is.False);
            Assert.That(flock.Leaving, Is.True, "Добили — стая улетает сразу.");
            Assert.That(beast.Hits.Count, Is.EqualTo(2));
        }

        [Test]
        public void SecondHitRenewsTheFlockInsteadOfAddingOne()
        {
            var beast = NewDummy();
            var flock = NewFlock();
            flock.Attach(beast, 1, 4, 0);
            Run(flock, 0, 3.05f);
            Assert.That(beast.Taken, Is.EqualTo(3));
            Assert.That(CrowFlock.On(beast), Is.SameAs(flock), "Эффект найдёт ту же стаю и продлит её.");
            flock.Renew(2, 4, 3.05f);
            Assert.That(flock.EndAt, Is.EqualTo(7.05f).Within(1e-4f), "Полный срок заново.");
            Assert.That(flock.DamagePerSecond, Is.EqualTo(2), "Урон — больший из двух.");
            Run(flock, 3.1f, 6.9f);
            Assert.That(beast.Distracted, Is.True, "Продлённая стая держит зверя дольше.");
            Run(flock, 7, 7.2f);
            Assert.That(beast.Distracted, Is.False);
            Assert.That(beast.Taken, Is.EqualTo(3 + 2 * 4), "Клевки на 4, 5, 6 и 7 с — по 2.");
            flock.Renew(3, 4, 7.3f);
            Assert.That(flock.Leaving, Is.True, "Улетевшую стаю не продлить — новая карта позовёт новых ворон.");
        }

        [Test]
        public void OnlyLivingWildCreaturesArePecked()
        {
            var beast = NewDummy();
            Assert.That(CrowsRules.CanPeck(beast, null), Is.True, "Мимик и любое существо карт.");
            Assert.That(CrowsRules.CanPeck(beast, beast), Is.False, "Не игрок.");
            Assert.That(CrowsRules.CanPeck(null, null), Is.False, "Промах — пшик.");
            beast.faction = Faction.Player;
            Assert.That(CrowsRules.CanPeck(beast, null), Is.False, "Союзников не трогает.");
            beast.faction = Faction.Wild;
            beast.health = 0;
            Assert.That(CrowsRules.CanPeck(beast, null), Is.False, "Мёртвого не клюют.");
        }

        [Test]
        public void MissedCrowsCircleAndFlyOff()
        {
            var flock = NewFlock();
            flock.Circle(Vector3.zero, 0);
            Assert.That(flock.Target, Is.Null);
            Assert.That(flock.Tick(flock.circleSeconds * .5f), Is.EqualTo(CrowsRules.Step.Wait), "Кружат.");
            Assert.That(flock.Tick(flock.circleSeconds), Is.EqualTo(CrowsRules.Step.Leave));
            Assert.That(flock.Tick(flock.circleSeconds + flock.leaveSeconds), Is.EqualTo(CrowsRules.Step.Done));
        }

        [Test]
        public void BeastsAndMimicsCanBeDistracted()
        {
            Assert.That(typeof(IDistractable).IsAssignableFrom(typeof(WorldCreature)), Is.True);
            Assert.That(typeof(IDistractable).IsAssignableFrom(typeof(ChestMimic)), Is.True);
        }

        [Test]
        public void FlapGoesBackAndForthThroughTheFrames()
        {
            var seen = new List<int>();
            for (int i = 0; i < 8; i++) seen.Add(CrowsRules.FlapFrame(i / 4f + .01f, 3));
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 1, 0, 1, 2, 1 }));
        }

        [Test]
        public void FlockPrefabHasThreeDrawnCrowsOnTheWorldGrid()
        {
            var prefab = Effect().flockPrefab;
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.crows.Length, Is.EqualTo(3), "Три вороны.");
            Assert.That(prefab.frames.Length, Is.GreaterThanOrEqualTo(2), "Машут крыльями.");
            var shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/HandDrawnSprite.mat");
            float grid = shared.GetFloat("_PixelsPerUnit");
            // Правило AGENTS.md «World sprite density»: все кадры — в одном атласе степени двойки, не больше 2048.
            var atlas = prefab.frames[0].texture;
            foreach (var frame in prefab.frames) Assert.That(frame.texture, Is.SameAs(atlas), "все кадры взмаха в одном атласе");
            Assert.That(Mathf.IsPowerOfTwo(atlas.width) && Mathf.IsPowerOfTwo(atlas.height), Is.True, $"атлас {atlas.width}×{atlas.height} — степень двойки");
            Assert.That(Mathf.Max(atlas.width, atlas.height), Is.LessThanOrEqualTo(2048));
            Assert.That(AssetDatabase.GetAssetPath(atlas), Is.EqualTo("Assets/Game/Prefabs/Cards/Crows/CrowFlapAtlas.png"));
            Assert.That(prefab.frames.Select(f => f.name), Is.EqualTo(new[] { "Crow flap 0", "Crow flap 1", "Crow flap 2" }), "кадры по порядку взмаха");
            foreach (var crow in prefab.crows)
            {
                Assert.That(crow, Is.Not.Null);
                Assert.That(crow.sharedMaterial, Is.SameAs(shared), "Рисованный спрайт, как все существа.");
                float scale = crow.transform.lossyScale.y;
                foreach (var frame in prefab.frames)
                    Assert.That(frame.pixelsPerUnit, Is.EqualTo(grid * scale).Within(1e-3f), "Одна плотность с общей сеткой (AGENTS.md).");
            }
            Assert.That(prefab.peckVfx, Is.Not.Null);
            Assert.That(prefab.featherVfx, Is.Not.Null);
            Assert.That(prefab.peckSound && prefab.cawSound && prefab.flapSound, Is.True);
            Assert.That(Effect().missSound, Is.Not.Null, "Промах — пшик.");
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, "Никаких коллайдеров в мире.");
            foreach (var source in prefab.GetComponentsInChildren<AudioSource>(true))
                Assert.That(source.enabled && source.playOnAwake, Is.False);
        }
    }
}
