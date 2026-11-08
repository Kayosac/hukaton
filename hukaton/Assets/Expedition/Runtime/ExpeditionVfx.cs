using UnityEngine;

namespace Hakaton.Expeditions
{
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
