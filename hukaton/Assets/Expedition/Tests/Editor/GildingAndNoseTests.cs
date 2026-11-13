using System.Linq;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Позолота» and «Нюх торгаша»: their rules and the assets that carry them.</summary>
    public sealed class GildingAndNoseTests
    {
        static readonly string[] Tiers = { "Chest 1 Beggar", "Chest 2 Common", "Chest 3 Sturdy", "Chest 4 Rich", "Chest 5 Legendary" };

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
        static TreasureChest Chest(string name) => Load<GameObject>("Assets/Expedition/Prefabs/Chests/" + name + ".prefab").GetComponent<TreasureChest>();

        [Test]
        public void GildingRaisesTheTierUpToTheTopAndNeverLowersIt()
        {
            Assert.That(GildingRules.NextRank(1, 1, 5), Is.EqualTo(2));
            Assert.That(GildingRules.NextRank(1, 2, 5), Is.EqualTo(3));
            Assert.That(GildingRules.NextRank(4, 2, 5), Is.EqualTo(5), "never above the richest");
            Assert.That(GildingRules.NextRank(5, 1, 5), Is.EqualTo(5));
            Assert.That(GildingRules.NextRank(3, 0, 5), Is.EqualTo(3));
            Assert.That(GildingRules.NextRank(5, 1, 3), Is.EqualTo(5), "a chest above the list's top keeps its tier");
        }

        [Test]
        public void GildingCoinsDoNotStackAndCountAsAChange()
        {
            Assert.That(GildingRules.CoinMultiplier(1, 1.5f), Is.EqualTo(1.5f));
            Assert.That(GildingRules.CoinMultiplier(1.5f, 1.5f), Is.EqualTo(1.5f), "a second II does not multiply again");
            Assert.That(GildingRules.CoinMultiplier(1.5f, 1), Is.EqualTo(1.5f), "a later I does not take it away");
            Assert.That(GildingRules.Changes(5, 5, 1, 1), Is.False, "legendary at I or III: nothing to do");
            Assert.That(GildingRules.Changes(5, 5, 1, 1.5f), Is.True, "legendary at II still pays more");
            Assert.That(GildingRules.Changes(5, 5, 1.5f, 1.5f), Is.False);
            Assert.That(GildingRules.Changes(2, 3, 1, 1), Is.True);
        }

        [Test]
        public void GildingAssetHasTheBoardNumbersAndEveryTier()
        {
            var effect = Load<GildingEffect>("Assets/Game/Data/Effects/Gilding.asset");
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.tierSteps.ToArray(), Is.EqualTo(new[] { 1, 1, 2 }));
            Assert.That(effect.coinMultiplier.At(1), Is.EqualTo(1));
            Assert.That(effect.coinMultiplier.At(2), Is.EqualTo(1.5f));
            Assert.That(effect.coinMultiplier.At(3), Is.EqualTo(2));
            Assert.That(effect.TopRank, Is.EqualTo(5));
            for (int rank = 1; rank <= 5; rank++)
            {
                var look = effect.LookOf(rank);
                Assert.That(look, Is.Not.Null, "tier " + rank);
                Assert.That(look.GetComponent<ChestMimic>(), Is.Null, "a plain chest, not a mimic: " + rank);
            }
            Assert.That(effect.showPrefab, Is.Not.Null);
            Assert.That(effect.showPrefab.sparkle && effect.showPrefab.swapBurst, "show effects");
            Assert.That(effect.impactVfx && effect.impactSound && effect.missSound && effect.mimicFlakes, "feedback");
            Assert.That(Load<CardDefinition>("Assets/Game/Data/gilding.asset").effectAsset, Is.EqualTo(effect));
        }

        [Test]
        public void EveryChestCanTakeEveryRicherLook()
        {
            foreach (var a in Tiers)
            foreach (var b in Tiers)
                Assert.That(ChestLook.Compatible(Chest(a), Chest(b)), a + " → " + b);
        }

        [Test]
        public void GildedChestTakesTheRicherModelBoxesAndMouth()
        {
            var poor = Chest(Tiers[0]);
            var rich = Chest(Tiers[4]);
            // In a preview scene: the open scene stays untouched.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var copy = ((GameObject)PrefabUtility.InstantiatePrefab(poor.gameObject, scene)).GetComponent<TreasureChest>();
            try
            {
                Assert.That(ChestLook.Copy(rich, copy), Is.True);
                Assert.That(copy.transform.Find(ChestLook.BodyName).GetComponent<MeshFilter>().sharedMesh,
                    Is.EqualTo(rich.transform.Find(ChestLook.BodyName).GetComponent<MeshFilter>().sharedMesh));
                Assert.That(copy.lid.Lid.GetComponent<MeshRenderer>().sharedMaterial, Is.EqualTo(rich.lid.Lid.GetComponent<MeshRenderer>().sharedMaterial));
                Assert.That(copy.lid.Lid.localPosition, Is.EqualTo(rich.lid.Lid.localPosition));
                Assert.That(copy.mouth.localPosition, Is.EqualTo(rich.mouth.localPosition));
                var boxes = copy.GetComponents<BoxCollider>();
                var richBoxes = rich.GetComponents<BoxCollider>();
                for (int i = 0; i < boxes.Length; i++) Assert.That(boxes[i].size, Is.EqualTo(richBoxes[i].size), "box " + i);
                Assert.That(copy.tier, Is.EqualTo(poor.tier), "the look alone does not change the tier (ChangeTier does)");
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void NoseReachesChestsWithinItsRadiusOnly()
        {
            Assert.That(MerchantNoseRules.Reaches(Vector3.zero, new Vector3(20, 0, 0), 20), Is.True);
            Assert.That(MerchantNoseRules.Reaches(Vector3.zero, new Vector3(14, 3, 14.5f), 20), Is.False);
            Assert.That(MerchantNoseRules.Reaches(Vector3.zero, new Vector3(0, 0, 49), 50), Is.True);
            Assert.That(MerchantNoseRules.Reaches(Vector3.zero, Vector3.zero, 0), Is.False);
            Assert.That(MerchantNoseRules.Until(30, 10, 40), Is.EqualTo(50), "lit again: lasts longer");
            Assert.That(MerchantNoseRules.Until(60, 10, 40), Is.EqualTo(60), "never shorter");
        }

        [Test]
        public void NoseAssetHasTheBoardNumbersAndAPillarSeenThroughWalls()
        {
            var effect = Load<MerchantNoseEffect>("Assets/Game/Data/Effects/Merchant nose.asset");
            Assert.That(effect, Is.Not.Null);
            Assert.That(new[] { effect.radius.At(1), effect.radius.At(2), effect.radius.At(3) }, Is.EqualTo(new[] { 20f, 40f, 70f }));
            Assert.That(effect.seconds, Is.EqualTo(40));
            Assert.That(effect.mimicShiverEvery.x, Is.GreaterThanOrEqualTo(6), "mimic hints stay rare");
            Assert.That(((ICardArea)effect).TryGetArea(null, 3, null, out float r) && Mathf.Approximately(r, 70));
            var beacon = effect.beaconPrefab;
            Assert.That(beacon && beacon.burst && beacon.burst.systems.Length > 0 && beacon.burst.systems.All(s => s), "pillar particles");
            var renderers = beacon.GetComponentsInChildren<ParticleSystemRenderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            foreach (var r2 in renderers)
                Assert.That(r2.sharedMaterials.All(m => m && m.shader.name == "Hakaton/Cards/Beacon through walls"), r2.name);
            Assert.That(effect.impactVfx && effect.impactSound && effect.missSound, "feedback");
            Assert.That(Load<CardDefinition>("Assets/Game/Data/merchant_nose.asset").effectAsset, Is.EqualTo(effect));
        }
    }
}
