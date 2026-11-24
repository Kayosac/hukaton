using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// The Old Apiary's finds are random every outing: what each one is (its pool), and for ordinary finds whether
    /// they turn up at all and where they lie — somewhere on the walkable ground near their spot. Valuable finds are
    /// always there, on the guarded spots where the level design hid them.
    /// </summary>
    public sealed class OldApiaryFindsPlayTests
    {
        const string PrefabPath = "Assets/OldApiary/Prefabs/Old Apiary.prefab";
        GameObject holder;

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
            holder = new GameObject("Apiary holder");
            holder.SetActive(false);
            var apiary = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), holder.transform);
            var creatures = apiary.transform.Find("Creatures");
            if (creatures) Object.DestroyImmediate(creatures.gameObject);
            holder.SetActive(true);
            yield return null;
            yield return new WaitForFixedUpdate();

            var finds = apiary.GetComponentsInChildren<WorldLoot>(true);
            Assert.That(finds.Length, Is.GreaterThanOrEqualTo(15), "the apiary's hand-placed finds");
            var ordinary = finds.Where(f => f.wander > 0).ToArray();
            var valuable = finds.Where(f => f.wander <= 0).ToArray();
            Assert.That(ordinary.Length, Is.GreaterThan(0), "some finds wander");
            Assert.That(valuable.Length, Is.GreaterThan(0), "some finds keep a guarded spot");
            Assert.That(ordinary.All(f => !f.guaranteed), "no ordinary find is there for sure");
            Assert.That(valuable.All(f => f.guaranteed), "every valuable find is there for sure");
            var spots = finds.ToDictionary(f => f, f => f.transform.position);

            int rolls = 0, wandering = 0, moved = 0, absent = 0;
            for (int outing = 1; outing <= 8; outing++)
            {
                var random = new System.Random(outing * 1637);
                foreach (var find in finds)
                {
                    find.ResetForOuting(random);
                    var shift = find.transform.position - spots[find];
                    shift.y = 0;
                    if (find.wander <= 0)
                    {
                        Assert.That(find.gameObject.activeSelf, find.name + " is there every outing");
                        Assert.That(shift.magnitude, Is.LessThan(.01f), find.name + " keeps its guarded spot");
                        continue;
                    }
                    rolls++;
                    if (!find.gameObject.activeSelf) absent++;
                    wandering++;
                    Assert.That(shift.magnitude, Is.LessThanOrEqualTo(find.wander + 1.6f), find.name + " stays near its spot");
                    Assert.That(Mathf.Abs(find.transform.position.y - spots[find].y), Is.LessThan(3f), find.name + " lies on the ground");
                    if (shift.magnitude > 1) moved++;
                }
            }
            Assert.That(moved, Is.GreaterThan(wandering / 3), $"ordinary finds lie somewhere new ({moved} of {wandering})");
            Assert.That(absent, Is.GreaterThan(0), "some ordinary finds are missing on some outings");
            Assert.That(absent, Is.LessThan(rolls / 3), $"most ordinary finds are there ({absent} of {rolls} missing)");
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
