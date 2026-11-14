using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Правила «Пороховой бочки»: числа по уровням, бочка — твёрдый предмет, а не боец; без огня (удар, время) не
    /// взрывается; поджиг (Огонь, Молния, общий поджиг зон) зажигает фитиль; мокрая не горит, вода гасит фитиль;
    /// взрыв подрывает соседние бочки с маленькой задержкой.
    /// </summary>
    public sealed class PowderBarrelTests
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
            var card = config.Find("powder_barrel");
            Assert.That(card, Is.Not.Null, "Пороховая бочка есть в каталоге.");
            return card;
        }

        static PowderBarrelEffect Effect()
        {
            var effect = Card().effectAsset as PowderBarrelEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        PowderBarrel Barrel(Vector3 at, int level = 1)
        {
            var effect = Effect();
            Assert.That(effect.barrelPrefab, Is.Not.Null, "У эффекта есть префаб бочки.");
            var barrel = Object.Instantiate(effect.barrelPrefab, at, Quaternion.identity);
            spawned.Add(barrel.gameObject);
            barrel.Setup(new PowderBarrelBlast { damage = effect.damage.At(level), playerDamage = effect.playerDamage.At(level), radius = effect.radius.At(level), knockback = effect.knockback.At(level) });
            Physics.SyncTransforms();
            return barrel;
        }

        [Test]
        public void CardHasTheTablesRankPriceAndUses()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.PowderBarrel));
            Assert.That((int)CardAction.PowderBarrel, Is.EqualTo(40));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((3, 220)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
            Assert.That(card.description, Is.EqualTo("Ставит бочку. От огня взрывается: ранит, поджигает и отбрасывает всех рядом."));
        }

        [Test]
        public void DamageRadiusAndKnockbackGrowWithTheLevel()
        {
            var effect = Effect();
            Assert.That((effect.damage.At(1), effect.damage.At(2), effect.damage.At(3)), Is.EqualTo((5, 8, 12)));
            Assert.That((effect.radius.At(1), effect.radius.At(2), effect.radius.At(3)), Is.EqualTo((3f, 4f, 5f)));
            Assert.That((effect.knockback.At(1), effect.knockback.At(2), effect.knockback.At(3)), Is.EqualTo((10f, 13f, 16f)));
            Assert.That(effect.TryGetStat(out var label, out var values, out _), Is.True);
            Assert.That(label, Is.EqualTo("Урон"));
            Assert.That((values.i, values.ii, values.iii), Is.EqualTo((5f, 8f, 12f)), "ПК показывает урон.");
        }

        [Test]
        public void ThePlayerTakesHisOwnSmallerNumberInHalfHearts()
        {
            var effect = Effect();
            Assert.That((effect.playerDamage.At(1), effect.playerDamage.At(2), effect.playerDamage.At(3)), Is.EqualTo((2, 3, 4)),
                "решение пользователя: игроку 2 / 3 / 4 половинки сердца");
            Assert.That((effect.damage.At(1), effect.damage.At(2), effect.damage.At(3)), Is.EqualTo((5, 8, 12)), "зверям — прежний урон");
            var barrel = Barrel(new Vector3(0, 100, 0), 3);
            Assert.That((barrel.Numbers.damage, barrel.Numbers.playerDamage), Is.EqualTo((12, 4)), "бочка получает оба числа своего уровня");
            Assert.That(typeof(PowderBarrel).GetField("playerBurn"), Is.Null, "игрока взрыв не поджигает: статуса «Горение» у бочки нет");
        }

        [Test]
        public void BurningGrassAndThicketsSetTheBarrelOff()
        {
            var barrel = Barrel(new Vector3(0, 100, 0));
            var far = Barrel(new Vector3(6, 100, 0));
            var registry = new CardZoneRegistry();
            registry.Add(barrel);
            registry.Add(far);
            // Горящий куст травы вплотную к бочке (ящик его вида в 0,2 м от бочки).
            var grass = new Bounds(barrel.Bounds.center + Vector3.left * (barrel.Bounds.extents.x + .45f), new Vector3(.5f, .6f, .5f));
            Assert.That(registry.IgniteBox(grass, .3f, IgnitionSource.Fire), Is.EqualTo(1));
            Assert.That(barrel.Lit, Is.True, "любой огонь игры поджигает бочку");
            Assert.That(far.Lit, Is.False, "дальняя — нет");
            Assert.That(registry.IgniteBox(grass, .3f, IgnitionSource.Fire), Is.Zero, "уже горит: повторный поджиг ничего не делает, цепочки огня кончаются");
            // Горючие того же поля поле поджигает само (skip).
            var other = Barrel(new Vector3(-6, 100, 0));
            registry.Add(other);
            var near = new Bounds(other.Bounds.center, Vector3.one);
            Assert.That(registry.IgniteBox(near, .3f, IgnitionSource.Fire, zone => ReferenceEquals(zone, other)), Is.Zero);
            Assert.That(other.Lit, Is.False);
        }

        [Test]
        public void TheBlastPushesEveryLooseThingNoMatterHowMany()
        {
            var at = new Vector3(4000, 300, -4000);
            var barrel = Barrel(at);
            for (int i = 0; i < 150; i++)
            {
                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spawned.Add(crate);
                crate.transform.localScale = Vector3.one * .1f;
                crate.transform.position = at + Quaternion.Euler(0, i * 137.5f, 0) * Vector3.forward * (.6f + i % 10 * .2f) + Vector3.up * (.2f + i / 10 * .1f);
                crate.AddComponent<Rigidbody>().useGravity = false;
            }
            Physics.SyncTransforms();
            Assert.That(barrel.PushLooseBodies(barrel.Centre), Is.EqualTo(150), "больше старого буфера на 64 — никого не пропустить");
        }

        [Test]
        public void AimRingShowsTheBlastRadius()
        {
            ICardArea area = Effect();
            for (int level = 1; level <= 3; level++)
            {
                Assert.That(area.TryGetArea(Card(), level, null, out float radius), Is.True);
                Assert.That(radius, Is.EqualTo(new[] { 3f, 4f, 5f }[level - 1]));
            }
            Assert.That(area.LeavesOutlinedZone, Is.False, "Стоящая бочка не рисует контур; только горящий фитиль.");
        }

        [Test]
        public void BarrelIsASolidPhysicalThingNotAFighter()
        {
            var barrel = Barrel(new Vector3(0, 100, 0));
            Assert.That(barrel.solid, Is.Not.Null);
            Assert.That(barrel.solid.isTrigger, Is.False, "Твёрдая: на неё можно забраться.");
            Assert.That(barrel.solid is MeshCollider mesh && mesh.convex, Is.True, "Выпуклый коллайдер под Rigidbody.");
            Assert.That(barrel.body, Is.Not.Null);
            Assert.That(barrel.body.isKinematic, Is.False, "Импульс её толкает.");
            Assert.That(barrel.solid.attachedRigidbody, Is.SameAs(barrel.body));
            Assert.That(Combat.Of(barrel.solid), Is.Null, "Не боец: звери её не атакуют, удар и камни её не берут.");
            Assert.That(barrel.GetComponentInChildren<Renderer>(), Is.Not.Null, "Видимая модель.");
            Assert.That(barrel.fuseTip, Is.Not.Null);
        }

        [Test]
        public void WithoutFireItNeverGoesOff()
        {
            var fuse = new BarrelFuse();
            Assert.That(fuse.Due(0), Is.False);
            Assert.That(fuse.Due(1e6f), Is.False, "Таймера нет: без огня бочка стоит вечно.");
            var barrel = Barrel(new Vector3(0, 100, 0));
            Assert.That(barrel.State, Is.EqualTo(BarrelFuseState.Idle));
            Assert.That(barrel.BlowAt, Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void FireOrLightningLightsAShortFuse()
        {
            var fire = Barrel(new Vector3(0, 100, 0));
            Assert.That(fire.Ignite(IgnitionSource.Fire, fire.Centre), Is.True);
            Assert.That(fire.State, Is.EqualTo(BarrelFuseState.Lit));
            float fuse = fire.BlowAt - Time.time;
            Assert.That(fuse, Is.InRange(.3f, .5f), "Фитиль 0,3–0,5 с: видно, что сейчас бабахнет.");
            Assert.That(fire.Ignite(IgnitionSource.Fire, fire.Centre), Is.False, "Уже горит.");
            Assert.That(fire.TryGetOutline(out _, out float radius), Is.True, "Пока горит фитиль — контур взрыва.");
            Assert.That(radius, Is.EqualTo(3));

            var lightning = Barrel(new Vector3(5, 100, 0));
            Assert.That(lightning.TryGetOutline(out _, out _), Is.False);
            Assert.That(lightning.Ignite(IgnitionSource.Lightning, lightning.Centre), Is.True, "Молния поджигает, как смолу.");
            Assert.That(lightning.Lit, Is.True);
        }

        [Test]
        public void SharedIgnitionOfCardZonesFindsTheBarrel()
        {
            var barrel = Barrel(new Vector3(0, 100, 0));
            var far = Barrel(new Vector3(8, 100, 0));
            var registry = new CardZoneRegistry();
            registry.Add(barrel);
            registry.Add(far);
            Assert.That(barrel.Kind, Is.EqualTo(CardZoneKind.PowderBarrel));
            // Огонь попал в землю рядом с бочкой (как FireEffect: точка, досягаемость 0,4 м).
            var near = barrel.Bounds.center + Vector3.right * (barrel.Bounds.extents.x + .3f);
            near.y = barrel.transform.position.y + .1f;
            Assert.That(registry.Ignite(near, near, 0, .4f, IgnitionSource.Fire), Is.EqualTo(1));
            Assert.That(barrel.Lit, Is.True);
            Assert.That(far.Lit, Is.False, "Дальняя бочка не загорелась.");
            // Горящая смола: огонь-цилиндр радиуса 1,5 м в метре от дальней бочки её достаёт.
            var tar = far.transform.position + Vector3.left * (1.5f + far.Bounds.extents.x + .1f);
            Assert.That(registry.Ignite(tar, tar + Vector3.up * 1.1f, 1.5f, .3f, IgnitionSource.Fire), Is.EqualTo(1));
            Assert.That(far.Lit, Is.True);
        }

        [Test]
        public void WetBarrelDoesNotCatchFireAndWaterPutsTheFuseOut()
        {
            var barrel = Barrel(new Vector3(0, 100, 0));
            Assert.That(barrel.Soak(null, 30), Is.True, "Струя мочит бочку.");
            Assert.That(barrel.Wet, Is.True);
            Assert.That(barrel.Ignite(IgnitionSource.Fire, barrel.Centre), Is.False, "Мокрая не горит.");
            Assert.That(barrel.Detonate(.1f), Is.False, "И от соседнего взрыва не горит.");
            Assert.That(barrel.State, Is.EqualTo(BarrelFuseState.Idle));

            var lit = Barrel(new Vector3(4, 100, 0));
            Assert.That(lit.Ignite(IgnitionSource.Fire, lit.Centre), Is.True);
            Assert.That(lit.Wash(null), Is.True, "Дождь гасит горящий фитиль.");
            Assert.That(lit.State, Is.EqualTo(BarrelFuseState.Idle));
            Assert.That(lit.Wet, Is.True);
        }

        [Test]
        public void BlastSetsOffNeighbourBarrelsWithAShortDelay()
        {
            var first = Barrel(new Vector3(0, 100, 0));
            var near = Barrel(new Vector3(2, 100, 0));
            var far = Barrel(new Vector3(9, 100, 0));
            var wet = Barrel(new Vector3(-2, 100, 0));
            wet.Soak(null, 30);
            var zones = new List<ICardZone> { first, near, far, wet };
            Assert.That(first.DetonateNeighbours(zones), Is.EqualTo(1));
            Assert.That(near.Lit, Is.True, "Соседняя бочка в радиусе — тоже взорвётся.");
            Assert.That(near.BlowAt - Time.time, Is.EqualTo(near.chainDelay).Within(1e-4f));
            Assert.That(near.chainDelay, Is.InRange(.05f, .3f), "Маленькая задержка цепи.");
            Assert.That(far.Lit, Is.False, "За радиусом — нет.");
            Assert.That(wet.Lit, Is.False, "Мокрая — нет.");
            Assert.That(first.Lit, Is.False, "Себя не поджигает.");
        }

        [Test]
        public void FuseRulesShortenButNeverLengthen()
        {
            var fuse = new BarrelFuse();
            Assert.That(fuse.Light(10, .4f, false), Is.True);
            Assert.That(fuse.Light(10.1f, .4f, false), Is.False, "Второй поджиг не отодвигает взрыв.");
            Assert.That(fuse.Light(10.1f, .1f, false), Is.True, "Цепь ускоряет.");
            Assert.That(fuse.BlowAt, Is.EqualTo(10.2f).Within(1e-4f));
            Assert.That(fuse.Due(10.19f), Is.False);
            Assert.That(fuse.Due(10.21f), Is.True);
            Assert.That(fuse.Blow(), Is.True);
            Assert.That(fuse.Blow(), Is.False);
            Assert.That(fuse.Light(11, .4f, false), Is.False, "Взорванная бочка больше не горит.");
        }

        [Test]
        public void BlastReachesAndThrowsAwayFromTheCentre()
        {
            var c = Vector3.zero;
            Assert.That(PowderBarrelRules.Reaches(c, 3, new Vector3(2.9f, 0, 0), new Vector3(2.9f, 1, 0)), Is.True);
            Assert.That(PowderBarrelRules.Reaches(c, 3, new Vector3(3.5f, 0, 0), new Vector3(3.5f, 1, 0)), Is.False);
            var close = PowderBarrelRules.Push(c, new Vector3(.1f, 0, 0), 3, 10, .5f, .45f);
            var edge = PowderBarrelRules.Push(c, new Vector3(3, 0, 0), 3, 10, .5f, .45f);
            Assert.That(close.magnitude, Is.EqualTo(10).Within(.2f), "Вплотную — полный отброс.");
            Assert.That(edge.magnitude, Is.EqualTo(5).Within(.01f), "У края — половина.");
            Assert.That(edge.x, Is.GreaterThan(0), "От центра наружу.");
            Assert.That(edge.y, Is.GreaterThan(0), "И вверх.");
            Assert.That(PowderBarrelRules.Shake(.9f, 0, 24), Is.EqualTo(.9f));
            Assert.That(PowderBarrelRules.Shake(.9f, 30, 24), Is.EqualTo(0));
        }

        [Test]
        public void OccupiedSpotLooksAroundStartingTowardTheThrower()
        {
            var first = PowderBarrelPlacement.Candidate(Vector3.zero, new Vector3(-5, 0, 0), .5f, 0, 0);
            Assert.That(first.x, Is.EqualTo(-.5f).Within(1e-4f), "Сперва — к бросавшему (у ног зверя, с нашей стороны).");
            Assert.That(PowderBarrelPlacement.TryFind(Vector3.zero, Vector3.forward, .5f, 1.5f, _ => null, out _), Is.False, "Негде — пшик.");
            int tries = 0;
            Assert.That(PowderBarrelPlacement.TryFind(Vector3.zero, Vector3.forward, .5f, 1.5f,
                p => ++tries == 5 ? p : (Vector3?)null, out var spot), Is.True);
            Assert.That(spot.magnitude, Is.EqualTo(.5f).Within(1e-4f));
        }
    }
}
