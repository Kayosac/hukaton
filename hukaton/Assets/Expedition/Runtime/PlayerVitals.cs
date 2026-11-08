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
        float invulnerableUntil;
        void Awake(){Restore();motor.Landed+=Landing;}
        void OnDestroy(){if(motor)motor.Landed-=Landing;}
        public void Restore(){HalfHearts=settings.maximumHalfHearts;Armour=0;invulnerableUntil=0;}
        public void Heal(int amount){HalfHearts=Mathf.Min(settings.maximumHalfHearts,HalfHearts+Mathf.Max(0,amount));LastChangeTime=Time.time;}
        public void Shield(int amount){Armour=Mathf.Max(Armour,amount);LastChangeTime=Time.time;}
        public bool Hurt(int halfHearts,Vector3 impulse)
        {
            if(!world.Running || HalfHearts==0 || Time.time<invulnerableUntil)return false;
            int absorbed=Mathf.Min(Armour,halfHearts);Armour-=absorbed;
            HalfHearts=Mathf.Max(0,HalfHearts-(halfHearts-absorbed));invulnerableUntil=Time.time+settings.hurtInvulnerability;
            LastChangeTime=Time.time;motor.ApplyImpulse(impulse);world.PlaySound(settings.hurtSound);
            if(HalfHearts==0)world.ReturnToShop(true);return true;
        }
        void Landing(float speed){if(speed>settings.fallDamageSpeed)Hurt(Mathf.Max(1,Mathf.FloorToInt((speed-settings.fallDamageSpeed)/3)),Vector3.zero);}
    }
}
