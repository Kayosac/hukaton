using Hakaton.Character;
using UnityEngine;
namespace Hakaton.Expeditions
{
    public sealed class PlayerVitals:MonoBehaviour
    {
        public ExpeditionSettings settings;
        public FirstPersonMotor motor;
        public ExpeditionWorld world;
        public int HalfHearts{get;private set;}
        public int Armour{get;private set;}
        public float LastChangeTime{get;private set;}
        public int PreviousHalfHearts{get;private set;}
        public bool LastChangeWasDamage{get;private set;}
        float invulnerableUntil;
        void Awake(){Restore();motor.Landed+=Landing;}
        void OnDestroy(){if(motor)motor.Landed-=Landing;}
        public void Restore()
        {
            HalfHearts=PreviousHalfHearts=settings.maximumHalfHearts;Armour=0;invulnerableUntil=0;
            LastChangeTime=float.NegativeInfinity;LastChangeWasDamage=false;
        }
        public void Heal(int amount)
        {
            int healed=Mathf.Min(settings.maximumHalfHearts,HalfHearts+Mathf.Max(0,amount));
            if(healed==HalfHearts)return;
            PreviousHalfHearts=HalfHearts;HalfHearts=healed;LastChangeTime=Time.time;LastChangeWasDamage=false;
        }
        public void Shield(int amount){Armour=Mathf.Max(Armour,amount);}
        public bool Hurt(int halfHearts,Vector3 impulse)
        {
            if(halfHearts<=0 || !world.Running || HalfHearts==0 || Time.time<invulnerableUntil)return false;
            PreviousHalfHearts=HalfHearts;LastChangeWasDamage=true;
            int absorbed=Mathf.Min(Armour,halfHearts);Armour-=absorbed;
            HalfHearts=Mathf.Max(0,HalfHearts-(halfHearts-absorbed));invulnerableUntil=Time.time+settings.hurtInvulnerability;
            LastChangeTime=Time.time;motor.ApplyImpulse(impulse);world.PlaySound(settings.hurtSound);
            if(HalfHearts==0)world.ReturnToShop(true);return true;
        }
        void Landing(float speed)
        {
            int damage=ExpeditionEffectRules.ImpactDamage(speed,settings.fallDamageSpeed,settings.fallDamageStep);
            if(damage>0)Hurt(damage,Vector3.zero);
        }
    }
}
