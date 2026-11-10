using UnityEngine;

namespace Hakaton.Loot
{
    /// <summary>
    /// One kind of loot: everything the game knows about it in one asset. The world pickup, the creature drop tables,
    /// the outing ledger, the shop catalogue and the shelf all read it from here. Add new items through
    /// Create → Hakaton → Лут → Предмет and put them into the <see cref="LootCatalog"/> (Assets/Loot/README.md).
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Лут/Предмет", fileName = "new_item", order = 0)]
    public sealed class LootItem : ScriptableObject
    {
        [Header("Учёт")]
        [Tooltip("Постоянный ключ: по нему лавка и сохранения узнают предмет. Латиница в нижнем регистре, цифры и _. " +
                 "Выпущенный id не менять: копии в сохранениях игроков пропадут.")]
        public string id;
        [Tooltip("Название в игре: строка подбора, список добычи (I), лавка.")]
        public string displayName;
        [Tooltip("Категория лавки, одна из списка категорий каталога: покупатели спрашивают товар по ней.")]
        public string category;
        [Tooltip("Рекомендованная цена в лавке, монеты. Её же показывает строка подбора.")]
        [Min(1)] public int price = 20;
        [Tooltip("Цвет блеска над предметом и строки подбора. На цену не влияет.")]
        public LootRarity rarity;

        [Header("Вид")]
        [Tooltip("Рисунок (PNG с Texture Type = Sprite, Mesh Type = Full Rect). Показывается там, где нет модели: " +
                 "у трофеев врагов обычно только он.")]
        public Sprite icon;
        [Tooltip("Видимая высота рисунка, м.")]
        [Min(.05f)] public float iconHeight = .45f;
        [Tooltip("3D-модель для мира: префаб только с мешами, без скриптов, коллайдеров и Rigidbody. " +
                 "Основание модели в нуле. Пусто — рисунок.")]
        public GameObject model;
        [Tooltip("Отдельная модель для витрины и руки продавца. Пусто — модель для мира, без неё рисунок.")]
        public GameObject shelfModel;

        [Header("Заметки")]
        [Tooltip("Только для разработки: откуда предмет берётся, идеи. В игре не показывается.")]
        [TextArea(2, 5)] public string notes;

        /// <summary>The shelf and the shopkeeper's hand show this, or the icon when it is empty.</summary>
        public GameObject ShelfModel => shelfModel ? shelfModel : model;
        public bool HasVisual => model || shelfModel || icon;

        void OnValidate()
        {
            if (id != null) id = id.Trim();
            if (displayName != null) displayName = displayName.Trim();
        }
    }
}
