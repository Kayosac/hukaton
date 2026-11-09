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
        [Header("World")]
        public SfxCue ambience;
        public SfxCue locationTitle;
    }
}
