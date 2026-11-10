using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Loot
{
    /// <summary>
    /// Every item the game knows, the shop categories and how loot looks. The shop registers exactly these items;
    /// an outing that brings back anything else is refused, so a new item must be listed here (the EditMode test
    /// LootCatalogTests checks it). One catalogue: Assets/Loot/Loot catalog.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Лут/Каталог", fileName = "Loot catalog", order = 1)]
    public sealed class LootCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class RarityStyle
        {
            public LootRarity rarity;
            [Tooltip("Цвет блеска над предметом и названия в строке подбора.")]
            public Color colour = Color.white;
            [Tooltip("Перекрашивать блеск над предметом. Выключено — блеск как в его префабе.")]
            public bool tintGlint = true;
        }

        [Tooltip("Все предметы игры. Порядок — порядок в лавке.")]
        public List<LootItem> items = new List<LootItem>();
        [Tooltip("Категории лавки. У каждого предмета одна из них.")]
        public List<string> categories = new List<string>();

        [Header("Вид")]
        [Tooltip("Префаб предмета-рисунка: LootSpriteView со SpriteRenderer. Им показываются предметы без модели.")]
        public LootSpriteView spriteView;
        [Tooltip("Цвета редкости.")]
        public RarityStyle[] rarities = new RarityStyle[0];

        Dictionary<string, LootItem> byId;
        int indexedCount = -1;

        /// <summary>The item with this id, or null.</summary>
        public LootItem Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (byId == null || indexedCount != items.Count) Index();
            return byId.TryGetValue(id, out var item) ? item : null;
        }

        public bool Contains(LootItem item) => item && items.Contains(item);

        public RarityStyle Style(LootRarity rarity)
        {
            if (rarities != null) foreach (var style in rarities) if (style != null && style.rarity == rarity) return style;
            return null;
        }

        public Color Colour(LootRarity rarity)
        {
            var style = Style(rarity);
            return style != null ? style.colour : Color.white;
        }

        /// <summary>
        /// Instantiates how the item looks under <paramref name="parent"/> at the parent's origin: the shelf model
        /// (shelf and hand) or the world model, or the drawing when there is no model. Visual only, no colliders added.
        /// </summary>
        public GameObject CreateVisual(LootItem item, Transform parent, bool forShelf)
        {
            if (!item) return null;
            GameObject source = forShelf ? item.ShelfModel : item.model;
            GameObject made;
            if (source) made = Instantiate(source, parent, false);
            else if (item.icon && spriteView)
            {
                var view = Instantiate(spriteView, parent, false);
                view.Show(item.icon, item.iconHeight);
                made = view.gameObject;
            }
            else
            {
                Debug.LogError($"Loot item '{item.name}' has neither a model nor an icon (or the catalogue has no sprite view).", item);
                return null;
            }
            made.name = item.displayName;
            return made;
        }

        void OnEnable() => byId = null;
        void OnValidate() => byId = null;

        void Index()
        {
            byId = new Dictionary<string, LootItem>(StringComparer.Ordinal);
            indexedCount = items.Count;
            foreach (var item in items)
                if (item && !string.IsNullOrEmpty(item.id) && !byId.ContainsKey(item.id)) byId.Add(item.id, item);
        }

#if UNITY_EDITOR
        [ContextMenu("Добавить все предметы проекта")]
        void CollectAllItems()
        {
            UnityEditor.Undo.RecordObject(this, "Collect loot items");
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(LootItem)))
            {
                var item = UnityEditor.AssetDatabase.LoadAssetAtPath<LootItem>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (item && !items.Contains(item)) items.Add(item);
            }
            items.RemoveAll(item => !item);
            byId = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
