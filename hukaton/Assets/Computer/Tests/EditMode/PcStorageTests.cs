using System.Linq;
using Hakaton.Computer;
using Hakaton.Loot;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Computer.Tests
{
    /// <summary>«Склад»: counting by kind, sorting, a picture for every catalogue item, the window wired in the PC prefab.</summary>
    public sealed class PcStorageTests
    {
        const string CatalogPath = "Assets/Loot/Loot catalog.asset";
        const string IconsPath = "Assets/Computer/Art/Stock/PcStockIcons.asset";
        const string PcPath = "Assets/Computer/Prefabs/BackroomPC.prefab";

        static PcStockInfo Info(string id)
        {
            switch (id)
            {
                case "tusk": return new PcStockInfo { Name = "Клык", Category = "Трофеи", Rarity = 0, Price = 120 };
                case "jug": return new PcStockInfo { Name = "Кувшин", Category = "Диковины", Rarity = 2, Price = 300 };
                case "honey": return new PcStockInfo { Name = "Банка мёда", Category = "Еда", Rarity = 2, Price = 90 };
                default: return new PcStockInfo { Name = "Агат", Category = "Находки", Rarity = 3, Price = 40 };
            }
        }

        [Test]
        public void CountsEveryKindInTheStoreroomAndOnTheDisplay()
        {
            var stored = new[] { "tusk", "jug", "tusk", "tusk" };
            var shelved = new[] { null, "jug", "", "honey", "tusk", null };
            var lines = PcStock.Count(stored, shelved, Info);

            Assert.That(lines.Select(l => l.Id), Is.EqualTo(new[] { "tusk", "jug", "honey" }), "one line per kind, in the order met; empty slots skipped");
            var tusk = lines[0];
            Assert.That((tusk.Stored, tusk.Shelved, tusk.Total), Is.EqualTo((3, 1, 4)));
            Assert.That((lines[1].Stored, lines[1].Shelved), Is.EqualTo((1, 1)));
            Assert.That((lines[2].Stored, lines[2].Shelved), Is.EqualTo((0, 1)), "a kind only on the display still shows");
            Assert.That(tusk.Name, Is.EqualTo("Клык"));
            Assert.That(tusk.Price, Is.EqualTo(120));
            Assert.That(lines.Sum(l => l.Stored), Is.EqualTo(stored.Length));
            Assert.That(lines.Sum(l => l.Shelved), Is.EqualTo(3));
        }

        [Test]
        public void NothingInTheShopMeansNoLines()
        {
            Assert.That(PcStock.Count(new string[0], new string[] { null, null }, Info), Is.Empty);
            Assert.That(PcStock.Count(null, null, Info), Is.Empty);
        }

        [Test]
        public void AnUnknownKindIsNamedByItsId()
        {
            var line = PcStock.Count(new[] { "mystery" }, null, null).Single();
            Assert.That(line.Name, Is.EqualTo("mystery"));
            Assert.That(line.Stored, Is.EqualTo(1));
        }

        [Test]
        public void SortsByEveryModeAndCyclesBack()
        {
            var lines = PcStock.Count(new[] { "tusk", "tusk", "jug", "agate", "agate", "agate" }, new[] { "honey", "jug" }, Info);
            string Order(PcStockSortMode mode) => string.Join(",", PcStock.Sort(lines, mode).Select(l => l.Id));

            Assert.That(Order(PcStockSortMode.Count), Is.EqualTo("agate,tusk,jug,honey"), "most in the storeroom first, then most on the display");
            Assert.That(Order(PcStockSortMode.Name), Is.EqualTo("agate,honey,tusk,jug"), "Агат, Банка мёда, Клык, Кувшин");
            Assert.That(Order(PcStockSortMode.Category), Is.EqualTo("jug,honey,agate,tusk"), "Диковины, Еда, Находки, Трофеи");
            Assert.That(Order(PcStockSortMode.Rarity), Is.EqualTo("agate,honey,jug,tusk"), "legendary first, ties by name");
            Assert.That(Order(PcStockSortMode.Price), Is.EqualTo("jug,tusk,honey,agate"));

            var mode = PcStockSortMode.Count;
            for (int i = 0; i < PcStock.Cycle.Length; i++) mode = PcStock.Next(mode);
            Assert.That(mode, Is.EqualTo(PcStockSortMode.Count));
            Assert.That(PcStock.Cycle, Is.EqualTo(new[] { PcStockSortMode.Count, PcStockSortMode.Name, PcStockSortMode.Category, PcStockSortMode.Rarity, PcStockSortMode.Price }));
        }

        [Test]
        public void FindsAreCountedInRussian()
        {
            Assert.That(PcStock.Finds(1), Is.EqualTo("1 находка"));
            Assert.That(PcStock.Finds(3), Is.EqualTo("3 находки"));
            Assert.That(PcStock.Finds(5), Is.EqualTo("5 находок"));
            Assert.That(PcStock.Finds(11), Is.EqualTo("11 находок"));
            Assert.That(PcStock.Finds(22), Is.EqualTo("22 находки"));
        }

        [Test]
        public void EveryCatalogueItemHasABakedStorageIcon()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootCatalog>(CatalogPath);
            var icons = AssetDatabase.LoadAssetAtPath<PcStockIcons>(IconsPath);
            Assert.That(catalog, Is.Not.Null, CatalogPath);
            Assert.That(icons, Is.Not.Null, IconsPath);
            // Every item, drawings included, gets an icon baked by the same pipeline (one look for the whole list).
            var missing = catalog.items.Where(item => item && !icons.Baked(item.id)).Select(item => item.id).ToList();
            Assert.That(missing, Is.Empty, "items without a baked «Склад» icon: " + string.Join(", ", missing));
            foreach (var item in catalog.items.Where(i => i))
                Assert.That(icons.For(item), Is.SameAs(icons.Baked(item.id)), item.id + ": the baked icon comes first");
            foreach (var entry in icons.baked)
            {
                Assert.That(entry.icon, Is.Not.Null, "baked entry without a sprite: " + entry.id);
                Assert.That(catalog.Find(entry.id), Is.Not.Null, "baked icon for an id the catalogue does not know: " + entry.id);
                var texture = entry.icon.texture;
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point), entry.id + ": pixel icon must use Point filtering");
                Assert.That(texture.mipmapCount, Is.EqualTo(1), entry.id + ": no mipmaps");
                Assert.That((texture.width, texture.height), Is.EqualTo((32, 32)), entry.id + ": one size for every icon");
            }
        }

        [Test]
        public void TheWindowIsWiredIntoThePc()
        {
            var root = PrefabUtility.LoadPrefabContents(PcPath);
            try
            {
                var pc = root.GetComponent<PcComputer>();
                var storage = root.GetComponentInChildren<PcStorageWindow>(true);
                Assert.That(storage, Is.Not.Null, "«Склад» window in " + PcPath);
                Assert.That(pc.storage, Is.SameAs(storage));
                Assert.That(storage.computer, Is.SameAs(pc));
                Assert.That(storage.window, Is.Not.Null);
                Assert.That(storage.window.title, Is.EqualTo("Склад"));
                Assert.That(storage.catalog, Is.Not.Null);
                Assert.That(storage.icons, Is.Not.Null);
                Assert.That(storage.rowPrefab, Is.Not.Null);
                Assert.That(new Object[] { storage.scroll, storage.rowsRoot, storage.header, storage.empty, storage.totals, storage.storedTotal,
                    storage.shelvedTotal, storage.kindsTotal, storage.pending, storage.sortButton, storage.sortLabel }, Has.None.Null);
                var program = pc.desktop.programs.SingleOrDefault(p => p.window == storage.window);
                Assert.That(program, Is.Not.Null, "«Склад» in PcDesktop.programs");
                Assert.That(program.icon && program.menuEntry && program.taskButton, "desktop icon, start menu entry, taskbar button");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
