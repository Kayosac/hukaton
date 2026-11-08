using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Hakaton.Expeditions
{
    [DefaultExecutionOrder(-225)]
    public sealed class CardThrower:MonoBehaviour
    {
        public ExpeditionWorld world;
        public FirstPersonInput input;
        public PlayerControlGate control;
        public LineRenderer trajectory;
        public ThrowPreparation Preparation{get;}=new ThrowPreparation();
        readonly int[] selection=new int[2];
        float nextDiscard;
        public CardCopy Selected {
            get {
                var trip=world.workshop.Model?.Current;if(trip==null || trip.Completed)return null;
                var hand=trip.Active.Hand;return hand.Count==0?null:hand[Mathf.Clamp(selection[(int)trip.ActiveKind],0,hand.Count-1)];
            }
        }
        void Update()
        {
            if(!world.Running || !control.GameplayActive){Cancel();return;}
            if(control.CapturedThisFrame)return;
            var keys=Keyboard.current;var mouse=Mouse.current;if(mouse==null)return;
            if(keys!=null) {
                if(keys.tabKey.wasPressedThisFrame)SwitchDeck();
                if(keys.rKey.wasPressedThisFrame)Discard();
                if(keys.eKey.wasPressedThisFrame && world.FocusedLoot)world.FocusedLoot.PickUp();
                var digits=new[]{keys.digit1Key,keys.digit2Key,keys.digit3Key,keys.digit4Key,keys.digit5Key};
                for(int i=0;i<digits.Length;i++)if(digits[i].wasPressedThisFrame)Select(i);
            }
            float wheel=mouse.scroll.ReadValue().y;if(Mathf.Abs(wheel)>.1f)Select(selection[(int)world.workshop.Model.Current.ActiveKind]+(wheel>0?-1:1));
            if(mouse.rightButton.wasPressedThisFrame){Cancel();return;}
            if(mouse.leftButton.wasPressedThisFrame)Begin();
            if(Preparation.Active) {
                bool modifier=keys!=null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);
                input.SetLookBlocked(this,modifier);
                Preparation.Tick(Time.deltaTime,modifier,mouse.delta.ReadValue().x,world.settings.chargeSeconds,world.settings.gesturePixels);
                if(mouse.leftButton.wasReleasedThisFrame)Release();
            }
            UpdateTrajectory();
        }
        public void Begin()
        {
            if(!world.Running || Selected==null)return;
            if(Selected.IsReturn){world.ReturnToShop(false);return;}
            Preparation.Begin();
        }
        public CardProjectile Release()
        {
            if(!Preparation.Active || !world.Running || Selected==null)return null;
            var card=Selected;float speed=Mathf.Lerp(world.settings.minimumSpeed,world.settings.maximumSpeed,Preparation.Charge),spin=Preparation.Spin;
            Cancel();if(world.workshop.Model.Current.Release(card.Id)==null)return null;
            var camera=world.viewCamera.transform;
            Vector3 origin=camera.position+camera.forward*.45f;
            var projectile=world.Launch(card,origin,camera.forward*speed+world.motor.PlanarVelocity*.25f,spin);
            if(Physics.SphereCast(camera.position,.08f,camera.forward,out var hit,.45f,~(1<<2),QueryTriggerInteraction.Ignore))
                projectile.Resolve(hit.collider,hit.point,hit.normal);
            world.PlaySound(world.settings.throwSound);world.hand.Refresh();return projectile;
        }
        public void Cancel(){Preparation.Cancel();if(input)input.SetLookBlocked(this,false);if(trajectory)trajectory.enabled=false;}
        public void ResetSelection(){Cancel();selection[0]=selection[1]=0;nextDiscard=0;}
        public void SwitchDeck()
        {
            if(!world.Running)return;Cancel();var trip=world.workshop.Model.Current;
            trip.Switch(trip.ActiveKind==DeckKind.Exploration?DeckKind.Combat:DeckKind.Exploration);world.hand.Refresh();world.PlaySound(world.settings.throwSound);
        }
        public void Select(int index)
        {
            if(!world.Running)return;var trip=world.workshop.Model.Current;int count=trip.Active.Hand.Count;if(count==0)return;
            Cancel();selection[(int)trip.ActiveKind]=(index%count+count)%count;world.hand.Refresh();
        }
        public void Discard()
        {
            if(!world.Running || Time.time<nextDiscard)return;
            var cards=world.workshop.Model.Current.Active.Hand.Where(c=>!c.IsReturn).ToArray();if(cards.Length==0)return;
            Cancel();world.workshop.Model.Current.DiscardHand();nextDiscard=Time.time+world.settings.discardCooldown;
            var camera=world.viewCamera.transform;
            for(int i=0;i<cards.Length;i++)world.Launch(cards[i],camera.position+camera.forward*.6f+camera.right*(i-2)*.08f,
                camera.forward*2+camera.right*(i-(cards.Length-1)*.5f)*3+Vector3.up*(2+i*.35f),0,true);
            world.hand.Refresh();world.PlaySound(world.settings.throwSound);
        }
        void UpdateTrajectory()
        {
            trajectory.enabled=Preparation.Active;if(!Preparation.Active)return;
            Vector3 point=world.viewCamera.transform.position+world.viewCamera.transform.forward*.45f;
            Vector3 velocity=world.viewCamera.transform.forward*Mathf.Lerp(world.settings.minimumSpeed,world.settings.maximumSpeed,Preparation.Charge)+world.motor.PlanarVelocity*.25f;
            int count=24;trajectory.positionCount=count;trajectory.SetPosition(0,point);
            for(int i=1;i<count;i++) {
                velocity+=(Vector3.down*world.settings.cardGravity+Vector3.Cross(Vector3.up,velocity.normalized)*Preparation.Spin*world.settings.curveAcceleration)*.07f;
                Vector3 next=point+velocity*.07f;
                if(Physics.Linecast(point,next,out var hit,~(1<<2),QueryTriggerInteraction.Collide)) {trajectory.SetPosition(i,hit.point);trajectory.positionCount=i+1;break;}
                point=next;trajectory.SetPosition(i,point);
            }
        }
        void OnDisable(){Cancel();}
    }
}
