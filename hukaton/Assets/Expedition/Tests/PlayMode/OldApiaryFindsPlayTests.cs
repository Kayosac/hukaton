using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// The Old Apiary's finds are random every outing: what each one is (its pool), whether it turns up at all, and
    /// for ordinary finds where it lies — somewhere on the walkable ground near its spot. Valuable finds keep their
    /// guarded spots, where the level design hid them.
    /// </summary>
    public sealed class OldApiaryFindsPlayTests
    {
        const string PrefabPath = "Assets/OldApiary/Prefabs/Old Apiary.prefab";
        const string FirstFind = "L11";
        const string CartSite = "Chest site 2 ";
        GameObject holder;

        /// <summary>The place without its creatures: only the ground, the navigation, the finds and the chest sites matter.</summary>
        IEnumerator Place()
        {
            holder = new GameObject("Apiary holder");
            holder.SetActive(false);
            var apiary = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), holder.transform);
            var creatures = apiary.transform.Find("Creatures");
            if (creatures) Object.DestroyImmediate(creatures.gameObject);
            holder.SetActive(true);
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        [TearDown]
        public void Clear()
        {
            if (holder) Object.Destroy(holder);
        }

        [UnityTest]
        public IEnumerator FindsChangeEveryOuting()
        {
            // The place without its creatures: only the ground, the navigation and the finds matter here.
            LogAssert.ignoreFailingMessages = true;
            yield return Place();
            var apiary = holder.transform.GetChild(0).gameObject;

            var finds = apiary.GetComponentsInChildren<WorldLoot>(true);
            Assert.That(finds.Length, Is.GreaterThanOrEqualTo(15), "the apiary's hand-placed finds");
            Assert.That(finds.Count(f => f.wander > 0), Is.GreaterThan(0), "some finds wander");
            // Ordinary finds may be missing; only L11, the first find of the first loop (~18 m from the deck), is there for sure.
            Assert.That(finds.Where(f => f.wander > 0 && f.name != FirstFind).All(f => !f.guaranteed), "ordinary finds are not there for sure");
            var spots = finds.ToDictionary(f => f, f => f.transform.position);

            int rolls = 0, wandering = 0, moved = 0, absent = 0;
            for (int outing = 1; outing <= 8; outing++)
            {
                var random = new System.Random(outing * 1637);
                foreach (var find in finds)
                {
                    find.ResetForOuting(random);
                    rolls++;
                    if (!find.gameObject.activeSelf) absent++;
                    var shift = find.transform.position - spots[find];
                    shift.y = 0;
                    if (find.wander <= 0)
                    {
                        Assert.That(shift.magnitude, Is.LessThan(.01f), find.name + " keeps its guarded spot");
                        continue;
                    }
                    wandering++;
                    Assert.That(shift.magnitude, Is.LessThanOrEqualTo(find.wander + 1.6f), find.name + " stays near its spot");
                    Assert.That(Mathf.Abs(find.transform.position.y - spots[find].y), Is.LessThan(3f), find.name + " lies on the ground");
                    if (shift.magnitude > 1) moved++;
                }
            }
            Assert.That(moved, Is.GreaterThan(wandering / 3), $"ordinary finds lie somewhere new ({moved} of {wandering})");
            Assert.That(absent, Is.GreaterThan(0), "some finds are missing on some outings");
            Assert.That(absent, Is.LessThan(rolls / 3), $"most finds are there ({absent} of {rolls} missing)");
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The first loop teaches rather than bites (balance stage 1): in every outing the first find L11 lies within 3 m of
        /// its spot and the broken cart has a chest — a beggar's or a common one, never a mimic. 50 outings, each with its
        /// own seed, rolled in the order the location uses (finds, then chest sites).
        /// </summary>
        [UnityTest]
        public IEnumerator FirstFindAndCartChestAreThereEveryOuting()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return Place();
            var apiary = holder.transform.GetChild(0).gameObject;
            var finds = apiary.GetComponentsInChildren<WorldLoot>(true);
            var first = finds.Single(f => f.name == FirstFind);
            var cart = apiary.GetComponentsInChildren<ChestSite>(true).Single(s => s.name.StartsWith(CartSite));
            var sites = apiary.GetComponentsInChildren<ChestSite>(true);
            Assert.That(first.guaranteed, FirstFind + " is there for sure");
            Assert.That(first.wander, Is.EqualTo(3f).Within(.01f), FirstFind + " wanders at most 3 m");
            var spot = first.transform.position;

            var ranks = new HashSet<int>();
            for (int outing = 1; outing <= 50; outing++)
            {
                var random = new System.Random(outing * 1637 + outing % 7);
                foreach (var find in finds) find.ResetForOuting(random);
                TreasureChest chest = null;
                foreach (var site in sites)
                {
                    var made = site.ResetForOuting(null, random);
                    if (site == cart) chest = made;
                }
                Assert.That(first.gameObject.activeSelf, $"outing {outing}: {FirstFind} is there");
                var shift = first.transform.position - spot;
                shift.y = 0;
                Assert.That(shift.magnitude, Is.LessThanOrEqualTo(3f + 1.6f), $"outing {outing}: {FirstFind} stays near its spot");
                Assert.That(chest, Is.Not.Null, $"outing {outing}: the cart has a chest");
                Assert.That(chest.GetComponent<ChestMimic>(), Is.Null, $"outing {outing}: the cart's chest is not a mimic");
                Assert.That(chest.tier.rank, Is.InRange(1, 2), $"outing {outing}: the cart's chest is a beggar's or a common one");
                ranks.Add(chest.tier.rank);
                yield return null;
            }
            Assert.That(ranks.Count, Is.EqualTo(2), "both a beggar's and a common chest turn up at the cart");
            foreach (var site in sites) site.Clear();
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
