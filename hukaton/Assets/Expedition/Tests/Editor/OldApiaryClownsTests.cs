using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Combat balance of the demo (stage 8): the Old Apiary keeps five floating clowns instead of ten, spread over the
    /// map; those that stood close to each other and to the first loop (cart, mushroom glade, apiary, shed, graves) went.
    /// </summary>
    public sealed class OldApiaryClownsTests
    {
        const string PrefabPath = "Assets/OldApiary/Prefabs/Old Apiary.prefab";
        static readonly string[] Kept = { "ClownEmoji_17", "ClownEmoji_x5", "ClownEmoji_x6", "ClownEmoji_x7", "ClownEmoji_x10" };
        static readonly string[] Gone = { "ClownEmoji_x2", "ClownEmoji_x3", "ClownEmoji_x4", "ClownEmoji_x8", "ClownEmoji_x9" };

        [Test]
        public void FiveClownsStayFarApart()
        {
            var apiary = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(apiary, Is.Not.Null, PrefabPath);
            var clownPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Floating clown emoji.prefab");
            var clowns = apiary.GetComponentsInChildren<WorldCreature>(true)
                .Where(c => PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) == clownPrefab).ToList();
            Assert.That(clowns.Select(c => c.name), Is.EquivalentTo(Kept), "five clowns are left");
            var names = apiary.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
            foreach (var gone in Gone)
                Assert.That(names.Any(n => n == gone || n.StartsWith(gone + " ")), Is.False, $"{gone} and its spawn points are gone");
            Vector2 Flat(Component c) { var p = apiary.transform.InverseTransformPoint(c.transform.position); return new Vector2(p.x, p.z); }
            float closest = float.MaxValue;
            for (int i = 0; i < clowns.Count; i++)
            {
                Assert.That(clowns[i].spawnPoints.Length, Is.EqualTo(2), clowns[i].name);
                for (int j = i + 1; j < clowns.Count; j++) closest = Mathf.Min(closest, Vector2.Distance(Flat(clowns[i]), Flat(clowns[j])));
            }
            Assert.That(closest, Is.GreaterThanOrEqualTo(40), "the clowns are spread over the map");
        }
    }
}
