using Hakaton.Decks;
using Hakaton.Decks.Unity;
using UnityEngine;
namespace Hakaton.Expeditions
{
    public sealed class CardProjectile:MonoBehaviour
    {
        public Rigidbody body;
        public Collider hitbox;
        public SpriteRenderer artwork;
        public TrailRenderer trail;
        ExpeditionWorld world;CardCopy card;CardDefinition definition;
        float spin,age;bool resolved,cosmetic;
        public bool Resolved=>resolved;
        public void Launch(ExpeditionWorld owner,CardCopy copy,Vector3 velocity,float curvature,bool discard=false)
        {
            world=owner;card=copy;definition=copy.IsReturn?null:owner.workshop.config.Find(copy.Spec.Id);
            artwork.sprite=copy.IsReturn?owner.workshop.config.returnArtwork:definition.artwork;
            body.linearVelocity=velocity;spin=curvature;cosmetic=discard;
            trail.startColor=definition?definition.accent:new Color(.9f,.8f,.4f);trail.endColor=new Color(trail.startColor.r,trail.startColor.g,trail.startColor.b,0);
            Physics.IgnoreCollision(hitbox,owner.motor.characterController);
        }
        void FixedUpdate()
        {
            if(resolved || !world)return;
            var velocity=body.linearVelocity;
            Vector3 side=Vector3.Cross(Vector3.up,velocity.normalized);
            body.AddForce(Vector3.down*world.settings.cardGravity+side*spin*world.settings.curveAcceleration,ForceMode.Acceleration);
            if(velocity.sqrMagnitude>.01f)body.MoveRotation(Quaternion.LookRotation(velocity.normalized));
        }
        void Update()
        {
            if(!world)return;
            age+=Time.deltaTime;artwork.transform.localRotation=Quaternion.Euler(0,0,age*(cosmetic?450:800));
            if(age>world.settings.projectileLife || (cosmetic && age>1.2f) || (!world.Running && !cosmetic))Destroy(gameObject);
        }
        void OnCollisionEnter(Collision hit){if(hit.contactCount>0)Resolve(hit.collider,hit.GetContact(0).point,hit.GetContact(0).normal);}
        void OnTriggerEnter(Collider hit){if(hit.GetComponentInParent<WorldObstacle>())Resolve(hit,transform.position,Vector3.up);}
        public void Resolve(Collider target,Vector3 point,Vector3 normal)
        {
            if(resolved || !world)return;resolved=true;
            if(!cosmetic && world.Running)world.ApplyImpact(card,definition,target,point,normal);
            Destroy(gameObject);
        }
    }
}
