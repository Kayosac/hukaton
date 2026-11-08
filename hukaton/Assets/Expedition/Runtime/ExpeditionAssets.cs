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
        public SfxCue cardSelect, deckSwitch, discard, charge, chargeReady, cancel, cardThrow, cardFlutter;
        [Header("Return")]
        public SfxCue returnAppears;
        public SfxCue returnCard;
        [Header("Player")]
        public SfxCue playerHurt;
        public SfxCue playerHeal, launch, landHeavy, heartbeat;
        [Header("Loot")]
        public SfxCue pickup;
        public SfxCue trophyDrop;
        [Header("World")]
        public SfxCue ambience;
        public SfxCue locationTitle;
    }

    /// <summary>Shared effect prefabs that are not tied to a specific card.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Effects")]
    public sealed class ExpeditionVfx : ScriptableObject
    {
        public VfxBurst paperPoof;
        public VfxBurst cardBurn;
        public VfxBurst pickupSparkle;
        public VfxBurst creatureDeath;
        public VfxBurst dustPuff;
        public VfxBurst landingDust;
        public VfxBurst returnPoof;
    }
}
