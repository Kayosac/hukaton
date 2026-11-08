using UnityEngine;
namespace Hakaton.Expeditions
{
    [CreateAssetMenu(menuName="Hakaton/Expedition/Settings")]
    public sealed class ExpeditionSettings:ScriptableObject
    {
        [Header("Card flight")]
        public float chargeSeconds=.65f,minimumSpeed=6,maximumSpeed=18,cardGravity=9.81f,curveAcceleration=12,gesturePixels=220;
        public float projectileLife=9,discardCooldown=.65f;
        [Header("World effects")]
        [Tooltip("Read radius, freeze duration and impulse from each card definition. Disabled in the original expedition sandbox.")]
        public bool useCardEffectValues;
        public float effectRadius=4.5f,windLift=17,windImpulse=12,freezeSeconds=4,curseSeconds=7;
        [Min(0)] public float collisionDamageSpeed=6;
        [Min(.1f)] public float collisionDamageStep=3;
        [Header("Player")]
        public int maximumHalfHearts=8;
        public float hurtInvulnerability=.75f,fallDamageSpeed=11,interactionReach=3.2f,returnSeconds=.85f;
        [Min(.1f)] public float fallDamageStep=3;
        [Range(0,1)]public float defeatLootLoss=.5f;
        [Header("Sound")]
        public AudioClip throwSound,impactSound,lootSound,hurtSound,creatureSound,returnSound;
    }
}
