using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// One line of «Склад»: the item's picture, name, rarity gem with category and rarity, how many lie in the
    /// storeroom (big), how many stand on the display, and the recommended price. A picture only, no input.
    /// </summary>
    public sealed class PcStockRow : MonoBehaviour
    {
        public Image background;
        [Tooltip("Even and odd rows, like an XP list view.")]
        public Color evenColour = Color.white, oddColour = new Color(.93f, .95f, .99f, 1);
        public Image icon;
        [Tooltip("Tinted with the rarity colour of the catalogue.")]
        public Image gem;
        public TMP_Text title, details, stored, shelved, price;
        [Tooltip("A count of zero is drawn in this colour (nothing left in the storeroom: all of it is on the display).")]
        public Color zeroColour = new Color(.55f, .55f, .55f, 1);
        public Color countColour = new Color(.1f, .1f, .1f, 1);

        public PcStockLine Line { get; private set; }

        public void Bind(PcStockLine line, Sprite picture, Color rarityColour, bool odd)
        {
            Line = line;
            if (background) background.color = odd ? oddColour : evenColour;
            if (icon)
            {
                icon.sprite = picture;
                icon.enabled = picture;
            }
            if (gem) gem.color = rarityColour;
            title.text = line.Name;
            string rarity = PcStock.RarityLabel(line.Rarity);
            details.text = string.IsNullOrEmpty(line.Category) ? rarity : line.Category + " · " + rarity;
            stored.text = line.Stored.ToString();
            stored.color = line.Stored > 0 ? countColour : zeroColour;
            shelved.text = line.Shelved.ToString();
            shelved.color = line.Shelved > 0 ? countColour : zeroColour;
            price.text = line.Price.ToString();
        }
    }
}
