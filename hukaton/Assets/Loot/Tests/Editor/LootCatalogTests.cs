using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Loot.Tests
{
    /// <summary>
    /// The loot content checks: run them after adding or changing an item, a drop table or a find
    /// (Window → General → Test Runner → EditMode → LootCatalogTests). Each failure names the asset to fix.
    /// </summary>
    public sealed class LootCatalogTests
    {
        static readonly Regex IdPattern = new Regex("^[a-z0-9_]+$");
        /// <summary>Pickups that get their item when the game runs.</summary>
        static readonly string[] Unbound = { "Assets/Loot/Prefabs/Loot pickup.prefab", "Assets/Loot/Prefabs/Loot drop.prefab" };

        static LootCatalog Catalog()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(LootCatalog));
            Assert.That(guids.Length, Is.EqualTo(1), "The project needs exactly one loot catalogue.");
            return AssetDatabase.LoadAssetAtPath<LootCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static IEnumerable<LootItem> ItemAssets() => AssetDatabase.FindAssets("t:" + nameof(LootItem))
            .Select(guid => AssetDatabase.LoadAssetAtPath<LootItem>(AssetDatabase.GUIDToAssetPath(guid)));

        static IEnumerable<(string path, GameObject prefab)> Prefabs() => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !path.StartsWith("Assets/Plugins/"))
            .Select(path => (path, AssetDatabase.LoadAssetAtPath<GameObject>(path)))
            .Where(entry => entry.Item2);

        [Test]
        public void EveryItemAssetIsInTheCatalogue()
        {
            var catalog = Catalog();
            var missing = ItemAssets().Where(item => !catalog.items.Contains(item)).Select(AssetDatabase.GetAssetPath).ToList();
            Assert.That(missing, Is.Empty, "Add these to Loot catalog (its ⋮ menu: «Добавить все предметы проекта»): " + string.Join(", ", missing));
            Assert.That(catalog.items.Any(item => !item), Is.False, "The catalogue has an empty row.");
            Assert.That(catalog.items.Distinct().Count(), Is.EqualTo(catalog.items.Count), "An item is listed twice.");
        }

        [Test]
        public void HeldModelsHaveReadableMeshes()
        {
            // The shopkeeper's hand frames a model by its silhouette, from its vertices (HeldStockView); in a build an
            // unreadable mesh gives only its box, and the model is held too small. Fix: the FBX's Read/Write on.
            var problems = new List<string>();
            foreach (var item in Catalog().items.Where(item => item && item.ShelfModel))
                foreach (var renderer in item.ShelfModel.GetComponentsInChildren<Renderer>(true))
                {
                    var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                    string path = mesh ? AssetDatabase.GetAssetPath(mesh) : null;
                    if (!mesh || mesh.isReadable || string.IsNullOrEmpty(path) || path.StartsWith("Library/")) continue;
                    problems.Add($"{AssetDatabase.GetAssetPath(item)}: {path} ({mesh.name})");
                }
            Assert.That(problems.Distinct(), Is.Empty, "Turn Read/Write on for these meshes");
        }

        [Test]
        public void ItemsAreComplete()
        {
            var catalog = Catalog();
            var problems = new List<string>();
            var ids = new Dictionary<string, LootItem>();
            foreach (var item in catalog.items.Where(item => item))
            {
                string where = AssetDatabase.GetAssetPath(item);
                if (string.IsNullOrEmpty(item.id) || !IdPattern.IsMatch(item.id)) problems.Add($"{where}: id «{item.id}» — only a-z, 0-9 and _.");
                else if (ids.TryGetValue(item.id, out var other)) problems.Add($"{where}: id «{item.id}» is already used by {AssetDatabase.GetAssetPath(other)}.");
                else ids.Add(item.id, item);
                if (string.IsNullOrWhiteSpace(item.displayName)) problems.Add($"{where}: no display name.");
                if (!catalog.categories.Contains(item.category)) problems.Add($"{where}: category «{item.category}» is not in the catalogue's categories.");
                if (item.price < 1) problems.Add($"{where}: price must be at least 1.");
                if (!item.HasVisual) problems.Add($"{where}: needs an icon or a model.");
                if (!item.model && !item.icon && item.shelfModel) problems.Add($"{where}: has only a shelf model; the world needs a model or an icon.");
                CheckModel(item.model, where, "model", problems);
                CheckModel(item.shelfModel, where, "shelf model", problems);
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        static void CheckModel(GameObject model, string where, string role, List<string> problems)
        {
            if (!model) return;
            if (model.GetComponentInChildren<WorldLoot>(true)) problems.Add($"{where}: the {role} is a pickup; use a look-only prefab (meshes) instead.");
            if (model.GetComponentInChildren<Rigidbody>(true)) problems.Add($"{where}: the {role} has a Rigidbody; models are look only.");
            if (!model.GetComponentInChildren<Renderer>(true)) problems.Add($"{where}: the {role} has nothing to render.");
        }

        [Test]
        public void CatalogueHasTheSpriteViewAndEveryRarityColour()
        {
            var catalog = Catalog();
            Assert.That(catalog.spriteView, Is.Not.Null, "Loot catalog: assign the sprite view prefab (Assets/Loot/Prefabs/Loot sprite.prefab).");
            Assert.That(catalog.spriteView.spriteRenderer, Is.Not.Null, "Loot sprite.prefab: assign its SpriteRenderer.");
            foreach (LootRarity rarity in System.Enum.GetValues(typeof(LootRarity)))
                Assert.That(catalog.Style(rarity), Is.Not.Null, $"Loot catalog: no colour for rarity {rarity}.");
            Assert.That(catalog.categories.Distinct().Count(), Is.EqualTo(catalog.categories.Count), "A category is listed twice.");
        }

        [Test]
        public void EveryPlacedFindIsACatalogueItem()
        {
            var catalog = Catalog();
            var problems = new List<string>();
            foreach (var (path, prefab) in Prefabs())
                foreach (var loot in prefab.GetComponentsInChildren<WorldLoot>(true))
                {
                    if (!loot.item)
                    {
                        if (!Unbound.Contains(path)) problems.Add($"{path} ({loot.name}): no item.");
                    }
                    else if (!catalog.Contains(loot.item)) problems.Add($"{path} ({loot.name}): {loot.item.name} is not in the catalogue.");
                    if (!loot.visual) problems.Add($"{path} ({loot.name}): assign Visual.");
                }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void CreatureDropTablesAreValid()
        {
            var catalog = Catalog();
            var problems = new List<string>();
            foreach (var (path, prefab) in Prefabs())
                foreach (var creature in prefab.GetComponentsInChildren<WorldCreature>(true))
                {
                    var entries = creature.drops?.entries;
                    if (entries == null) continue;
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var entry = entries[i];
                        string where = $"{path} ({creature.name}), drop {i + 1}";
                        if (entry == null || !entry.item) { problems.Add($"{where}: no item."); continue; }
                        if (!catalog.Contains(entry.item)) problems.Add($"{where}: {entry.item.name} is not in the catalogue.");
                        if (entry.chance <= 0) problems.Add($"{where}: chance is 0, it never drops.");
                        if (entry.max < entry.min) problems.Add($"{where}: max is below min.");
                    }
                }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void FindPoolsHoldOnlyCatalogueItemsAndAreNotEmpty()
        {
            var catalog = Catalog();
            var problems = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LootPool)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pool = AssetDatabase.LoadAssetAtPath<LootPool>(path);
                if (pool.IsEmpty) problems.Add($"{path}: the pool has no item with a weight above 0.");
                foreach (var entry in pool.entries)
                {
                    if (entry == null || !entry.item) { problems.Add($"{path}: an entry has no item."); continue; }
                    if (!catalog.Contains(entry.item)) problems.Add($"{path}: {entry.item.name} is not in the catalogue.");
                    if (!entry.item.model) problems.Add($"{path}: {entry.item.name} has no world model.");
                }
            }
            foreach (var (path, prefab) in Prefabs())
                foreach (var loot in prefab.GetComponentsInChildren<WorldLoot>(true))
                    if (loot.pool && loot.pool.IsEmpty) problems.Add($"{path} ({loot.name}): its pool is empty.");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void PoolPickFollowsTheWeights()
        {
            var a = ScriptableObject.CreateInstance<LootItem>();
            var b = ScriptableObject.CreateInstance<LootItem>();
            var pool = ScriptableObject.CreateInstance<LootPool>();
            try
            {
                pool.entries.Add(new LootPool.Entry { item = a, weight = 3 });
                pool.entries.Add(new LootPool.Entry { item = b, weight = 1 });
                Assert.That(pool.Pick(0.0), Is.SameAs(a));
                Assert.That(pool.Pick(0.74), Is.SameAs(a));
                Assert.That(pool.Pick(0.76), Is.SameAs(b));
                Assert.That(pool.Pick(1.0), Is.SameAs(b));
                var random = new System.Random(5);
                int countA = 0;
                for (int i = 0; i < 4000; i++) if (pool.Pick(random) == a) countA++;
                Assert.That(countA / 4000f, Is.EqualTo(.75f).Within(.03f));
                Assert.That(pool.Pick(new System.Random(42)), Is.SameAs(pool.Pick(new System.Random(42))), "Same seed, same item.");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(pool);
            }
        }
    }
}
