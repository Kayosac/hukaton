using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Every sound cue of an outing in one Inspector-tunable asset.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Audio")]
    public sealed class ExpeditionAudio : ScriptableObject
    {
        [Header("Hand")]
        public SfxCue cardDraw;
        public SfxCue cardSelect, discard, charge, chargeReady, cancel, cardThrow, cardFlutter;
        [Header("Return")]
        public SfxCue returnAppears;
        public SfxCue returnCard;
        [Header("Player")]
        public SfxCue playerHurt;
        public SfxCue playerHeal, launch, landHeavy, heartbeat;
        [Tooltip("Body falling into deep water: the drowning splash.")]
        public SfxCue drownSplash;
        [Header("Loot")]
        public SfxCue pickup;
        public SfxCue trophyDrop;
        [Tooltip("A coin from a chest landing in the bag; the pitch climbs during a quick run of coins.")]
        public SfxCue coinPickup;
        [Header("World")]
        public SfxCue ambience;
        public SfxCue locationTitle;
        [Header("Loose things hitting something (PropImpacts)")]
        public SfxCue impactWood;
        public SfxCue impactClay, impactStone, impactMetal, impactSoft, impactCoin;

        /// <summary>The knock of a loose thing of this make; wood when its own is not set.</summary>
        public SfxCue Impact(PropSurface surface)
        {
            SfxCue cue = surface switch
            {
                PropSurface.Clay => impactClay,
                PropSurface.Stone => impactStone,
                PropSurface.Metal => impactMetal,
                PropSurface.Soft => impactSoft,
                PropSurface.Coin => impactCoin,
                _ => impactWood,
            };
            return cue ? cue : impactWood;
        }
    }
}
