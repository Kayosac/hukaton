using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>Every sound of the shop phase in one Inspector-tunable asset.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Trading/Shop audio")]
    public sealed class ShopAudio : ScriptableObject
    {
        [Header("Visitors")]
        public SfxCue doorBell;
        public SfxCue npcNeutral, npcHappy, npcAngry, walkOut;
        [Header("Money")]
        public SfxCue saleSmall;
        public SfxCue saleBig, coinTick;
        [Header("Shop")]
        public SfxCue placeItem;
        public SfxCue takeItem, slotHover, clockTick, shopOpen, shopClose, priceTap, ambience;
    }
}
