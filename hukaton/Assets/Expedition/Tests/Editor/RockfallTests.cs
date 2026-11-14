using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Правила «Камнепада»: числа по уровням, зона и её контур, кого достают камни, кто «мелкий».</summary>
    public sealed class RockfallTests
    {
        static CardDefinition Card()
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            var card = config.Find("rockfall");
            Assert.That(card, Is.Not.Null, "Камнепад есть в каталоге.");
            return card;
        }

        static RockfallEffect Effect()
        {
            var effect = Card().effectAsset as RockfallEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        [Test]
        public void CardHasTheTablesRankPriceAndUses()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.Rockfall));
            Assert.That((int)CardAction.Rockfall, Is.EqualTo(37));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((2, 170)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
        }

        [Test]
        public void DamageAndRadiusGrowWithTheLevel()
        {
            var effect = Effect();
            Assert.That((effect.damage.At(1), effect.damage.At(2), effect.damage.At(3)), Is.EqualTo((3, 4, 6)));
            Assert.That((effect.radius.At(1), effect.radius.At(2), effect.radius.At(3)), Is.EqualTo((3f, 4f, 5f)));
            Assert.That(effect.delay, Is.EqualTo(.5f));
            Assert.That(effect.smallStunSeconds, Is.EqualTo(1));
            Assert.That(effect.TryGetStat(out var label, out var values, out _), Is.True);
            Assert.That(label, Is.EqualTo("Урон"));
            Assert.That((values.i, values.ii, values.iii), Is.EqualTo((3f, 4f, 6f)), "ПК показывает урон.");
        }

        [Test]
        public void ThePlayerTakesHisOwnSmallerNumberInHalfHearts()
        {
            var effect = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset").Find("rockfall").effectAsset as RockfallEffect;
            Assert.That(effect, Is.Not.Null);
            Assert.That((effect.playerDamage.At(1), effect.playerDamage.At(2), effect.playerDamage.At(3)), Is.EqualTo((1, 2, 3)),
                "решение пользователя: игроку 1 / 2 / 3 половинки сердца");
            Assert.That((effect.damage.At(1), effect.damage.At(2), effect.damage.At(3)), Is.EqualTo((3, 4, 6)), "зверям — прежний урон");
        }

        [Test]
        public void AimRingFollowsTheLevelAndTheZoneOutlinesItself()
        {
            ICardArea area = Effect();
            for (int level = 1; level <= 3; level++)
            {
                Assert.That(area.TryGetArea(Card(), level, null, out float radius), Is.True);
                Assert.That(radius, Is.EqualTo(new[] { 3f, 4f, 5f }[level - 1]));
            }
            Assert.That(area.LeavesOutlinedZone, Is.True, "Контур на земле рисует зона, пока камни летят.");
        }

        [Test]
        public void StonesReachOnlyThoseInsideTheZone()
        {
            var centre = new Vector3(10, 2, -4);
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre), Is.True);
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre + new Vector3(2.9f, 0, 0)), Is.True);
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre + new Vector3(2.2f, .3f, 2.2f)), Is.False, "3,11 м по горизонтали — снаружи.");
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre + new Vector3(0, -.8f, 1)), Is.True, "Чуть ниже на склоне.");
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre + new Vector3(0, 3f, 0)), Is.True, "Низко летящий рой.");
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre + new Vector3(0, 5f, 0)), Is.False, "Высоко над зоной.");
            Assert.That(RockfallRules.Reaches(centre, 3, 1, 3.5f, centre + new Vector3(0, -2f, 0)), Is.False, "В яме под зоной.");
        }

        [Test]
        public void SmallMeansNotHeavierThanThePlayer()
        {
            Assert.That(RockfallRules.IsSmall(.3f, 1), Is.True);
            Assert.That(RockfallRules.IsSmall(1, 1), Is.True);
            Assert.That(RockfallRules.IsSmall(1.2f, 1), Is.False);
            Assert.That(RockfallRules.IsSmall(0, 1), Is.False, "Нет тела — не оглушаем.");
            Assert.That(Effect().smallMaxMass, Is.EqualTo(1));
            Assert.That(IsSmall("Purple mushroom"), Is.True);
            Assert.That(IsSmall("Kind little worm"), Is.True);
            Assert.That(IsSmall("Rat pumpkin"), Is.True);
            Assert.That(IsSmall("Goblin dog"), Is.False);
            Assert.That(IsSmall("Mantis man"), Is.False);
            Assert.That(IsSmall("Giant vile toad"), Is.False);
        }

        static bool IsSmall(string creature)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<WorldCreature>($"Assets/Game/Prefabs/{creature}.prefab");
            Assert.That(prefab, Is.Not.Null, creature);
            Assert.That(prefab.body, Is.Not.Null, creature);
            return RockfallRules.IsSmall(prefab.body.mass, Effect().smallMaxMass);
        }

        [Test]
        public void PushGoesOutwardAndALittleUp()
        {
            var push = RockfallRules.Push(Vector3.zero, new Vector3(3, -1, 4), 5, .2f);
            Assert.That(Vector3.Distance(push, new Vector3(3, 1, 4)), Is.LessThan(1e-4f));
            push = RockfallRules.Push(Vector3.zero, Vector3.zero, 2, .5f);
            Assert.That(Vector3.Distance(push, Vector3.up), Is.LessThan(1e-4f), "В самом центре — только вверх.");
        }

        [Test]
        public void MoreStonesForABiggerZoneWithinLimits()
        {
            Assert.That(RockfallRules.StoneCount(3, 1.1f, 12, 60), Is.EqualTo(31));
            Assert.That(RockfallRules.StoneCount(5, 1.1f, 12, 60), Is.EqualTo(60));
            Assert.That(RockfallRules.StoneCount(.5f, 1.1f, 12, 60), Is.EqualTo(12));
        }

        [Test]
        public void ZonePrefabLeavesNothingSolid()
        {
            var zone = Effect().zonePrefab;
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone.stones, Is.Not.Null, "Падающие камни — частицы.");
            Assert.That(zone.crashVfx, Is.Not.Null);
            Assert.That(zone.crashSound, Is.Not.Null);
            Assert.That(zone.GetComponentsInChildren<Collider>(true), Is.Empty, "Ни одного коллайдера: камни только видимость.");
            Assert.That(zone.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(zone.crashVfx.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (var source in zone.crashVfx.GetComponentsInChildren<AudioSource>(true))
                Assert.That(source.enabled && source.playOnAwake, Is.False, "Звук обвала — только SfxCue зоны.");
        }

        [Test]
        public void StonesAreCrookedWorldRocksFallingStraightIntoTheRing()
        {
            var zone = Effect().zonePrefab;
            var renderer = zone.stones.GetComponent<ParticleSystemRenderer>();
            Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh), "Камни — объёмные, не плоские спрайты.");
            Assert.That(renderer.meshCount, Is.GreaterThanOrEqualTo(3), "Несколько разных кривых камней.");
            var meshes = new Mesh[renderer.meshCount];
            renderer.GetMeshes(meshes);
            foreach (var mesh in meshes)
            {
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.bounds.center.magnitude, Is.LessThan(.01f), mesh.name + ": крутится вокруг своей середины.");
            }
            Assert.That(renderer.sharedMaterial.name, Is.EqualTo("M_Forest_Rock"), "Материал — как у камней мира.");
            Assert.That(zone.stones.main.startRotation3D, Is.True);
            Assert.That(zone.stones.rotationOverLifetime.enabled, Is.True, "Камни крутятся в полёте.");

            // Отвесно и не дальше кольца: без конуса и сферического разлёта, с запасом на крупный камень и пыль.
            var shape = zone.stones.shape;
            Assert.That(shape.angle, Is.Zero);
            Assert.That(shape.sphericalDirectionAmount, Is.Zero);
            Assert.That(shape.randomDirectionAmount, Is.Zero);
            Assert.That(zone.stonesSpread, Is.LessThanOrEqualTo(.75f));
            Assert.That(zone.stones.main.startSize.constantMax * .5f + zone.stonesSpread * 3, Is.LessThan(3 - .4f),
                "Край крупного камня у самого маленького кольца (3 м) — с запасом на обломки и пыль.");
            // Коснувшись земли, камень пропадает (пыль и обломки), не отскакивает за кольцо и не остаётся.
            Assert.That(zone.stones.collision.enabled, Is.True);
            Assert.That(zone.stones.collision.lifetimeLoss.constant, Is.EqualTo(1));
        }
    }
}
