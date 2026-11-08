using UnityEngine;
namespace Hakaton.Expeditions
{
    [CreateAssetMenu(menuName="Hakaton/Expedition/Settings")]
    public sealed class ExpeditionSettings:ScriptableObject
    {
        [Header("Card flight")]
        public float chargeSeconds=1.15f,minimumSpeed=8,maximumSpeed=29,cardGravity=7,curveAcceleration=12,gesturePixels=220;
        public float projectileLife=9,discardCooldown=.65f;
        [Header("World effects")]
        public float effectRadius=4.5f,windLift=17,windImpulse=12,freezeSeconds=4,curseSeconds=7;
        [Header("Player")]
        public int maximumHalfHearts=10;
        public float hurtInvulnerability=.75f,fallDamageSpeed=20,interactionReach=3.2f,returnSeconds=.85f;
        [Range(0,1)]public float defeatLootLoss=.5f;
        [Header("Sound")]
        public AudioClip throwSound,impactSound,lootSound,hurtSound,creatureSound,returnSound;
    }
}
