using Hakaton.Feel;
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
        [Tooltip("Stars circling a stunned creature's head; held for the stun.")]
        public VfxBurst stunned;
        [Tooltip("Drops falling from a wet body (WetStatus); held while it stays wet.")]
        public VfxBurst wetDrips;
        [Tooltip("The ice shell a wet creature hit by Ice is encased in (IceEncasement): meshes, shards, effects.")]
        public IceShellKit iceShell;
    }
}
