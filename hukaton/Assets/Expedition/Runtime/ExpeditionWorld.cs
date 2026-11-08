using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Owns a single outing and its authored world. Spawns only configured gameplay prefabs.</summary>
    public sealed class ExpeditionWorld:MonoBehaviour
    {
        public DeckPrototype workshop;
        public ExpeditionSettings settings;
        public FirstPersonMotor motor;
        public PlayerControlGate control;
        public PlayerVitals vitals;
        public Camera viewCamera;
        public CardThrower thrower;
        public ExpeditionHud hud;
        public WorldHand hand;
        public Transform departurePose,shopPose,spawnedObjects;
        public CardProjectile projectilePrefab;
        public WorldImpact impactPrefab;
        public WorldLoot[] finds;
        public WorldObstacle[] obstacles;
        public WorldCreature[] creatures;
        public PhysicsProp[] props;
        public AudioSource audioSource;
        readonly List<LootSave> loot=new List<LootSave>();
        public IReadOnlyList<LootSave> Loot=>loot.AsReadOnly();
        public bool Returning{get;private set;}
        public bool Running=>workshop.Model!=null && workshop.Model.IsAway && !Returning;
        public int Impacts{get;private set;}
        public WorldLoot FocusedLoot{get;private set;}
        public string Notice{get;private set;}
        public float NoticeUntil{get;private set;}
        void Awake(){workshop.Departed+=BeginOuting;}
        void OnDestroy(){if(workshop)workshop.Departed-=BeginOuting;}
        public void BeginOuting()
        {
            Returning=false;Impacts=0;loot.Clear();
            foreach(Transform child in spawnedObjects)Destroy(child.gameObject);
            var random=new System.Random(workshop.Model.Journeys*1637);
            foreach(var obstacle in obstacles)obstacle.ResetForOuting();
            foreach(var find in finds)find.ResetForOuting(random);
            foreach(var prop in props)prop.ResetForOuting();
            foreach(var creature in creatures)creature.ResetForOuting(random);
            vitals.Restore();motor.Teleport(departurePose.position,departurePose.rotation);
            thrower.ResetSelection();control.CaptureCursor();hud.canvas.enabled=true;hand.Refresh();
            Announce("ЛКМ: бросок  ·  Tab: колода  ·  R: сброс руки",7);
        }
        void Update()
        {
            FocusedLoot=null;if(!Running)return;
            if(motor.transform.position.y< -8){vitals.Hurt(settings.maximumHalfHearts,Vector3.zero);return;}
            if(control.GameplayActive && Physics.Raycast(viewCamera.transform.position,viewCamera.transform.forward,out var hit,settings.interactionReach,~(1<<2),QueryTriggerInteraction.Ignore))
                FocusedLoot=hit.collider.GetComponentInParent<WorldLoot>();
        }
        public void Collect(string name,int value)=>Collect(null,name,value);
        public void Collect(string itemId,string name,int value)
        {
            if(!Running)return;
            var entry=loot.Find(x=>!string.IsNullOrEmpty(itemId)?x.id==itemId:string.IsNullOrEmpty(x.id) && x.name==name && x.value==value);
            if(entry==null)loot.Add(new LootSave{id=itemId,name=name,value=value,count=1});else entry.count++;
            PlaySound(settings.lootSound);Announce("Взято: "+name);
        }
        public void Announce(string text,float seconds=3){Notice=text;NoticeUntil=Time.time+seconds;}
        public void PlaySound(AudioClip sound){if(sound)audioSource.PlayOneShot(sound,.7f);}
        public CardProjectile Launch(CardCopy card,Vector3 position,Vector3 velocity,float spin,bool cosmetic=false)
        {
            var projectile=Instantiate(projectilePrefab,position,Quaternion.LookRotation(velocity.normalized),spawnedObjects);
            projectile.Launch(this,card,velocity,spin,cosmetic);return projectile;
        }
        public void ApplyImpact(CardCopy copy,CardDefinition definition,Collider target,Vector3 point,Vector3 normal)
        {
            if(!Running || !definition)return;Impacts++;PlaySound(settings.impactSound);
            var fx=Instantiate(impactPrefab,point+normal*.05f,Quaternion.LookRotation(normal),spawnedObjects);fx.SetColour(definition.accent);
            var obstacle=target.GetComponentInParent<WorldObstacle>();if(obstacle)obstacle.Apply(definition.action);
            var creature=target.GetComponentInParent<WorldCreature>();
            float radius=settings.useCardEffectValues?definition.effectRadius:settings.effectRadius+(copy.Level-1)*.6f;
            switch(definition.action)
            {
                case CardAction.Fire:
                    if(creature)creature.Damage(copy.Power,settings.useCardEffectValues?Vector3.zero:normal*-2,settings.useCardEffectValues?0:2+copy.Level);
                    break;
                case CardAction.Freeze:
                    if(creature)creature.Freeze(settings.useCardEffectValues?copy.Power:settings.freezeSeconds+copy.Level);
                    break;
                case CardAction.Wind:
                    ApplyWind(copy,definition,point,radius);
                    break;
                case CardAction.Heal:
                    if(Vector3.Distance(motor.transform.position,point)<radius){vitals.Heal(copy.Power);Announce("Здоровье восстановлено");}
                    break;
                case CardAction.Stone:
                    if(Vector3.Distance(motor.transform.position,point)<radius){vitals.Shield(copy.Power);Announce("Каменная защита");}
                    if(creature)creature.Damage(2,normal*-copy.Power,0);
                    break;
                case CardAction.Curse:
                    if(creature)creature.Curse(settings.curseSeconds+copy.Level,copy.Power);
                    break;
                case CardAction.Strike:
                    if(creature)creature.Damage(copy.Power,Vector3.zero,0);
                    break;
                case CardAction.HeavyStrike:
                    if(creature)creature.Damage(copy.Power,-normal.normalized*definition.ImpulseAtLevel(copy.Level),0);
                    break;
            }
        }
        void ApplyWind(CardCopy copy,CardDefinition definition,Vector3 point,float radius)
        {
            float impulse=definition.ImpulseAtLevel(copy.Level);
            // The motor's normalized mass is one. Its centre is above the foot origin,
            // so a card landing under the player produces an upward radial impulse.
            Vector3 playerCentre=motor.transform.TransformPoint(motor.characterController.center);
            if(Vector3.Distance(motor.transform.position,point)<=radius)
                motor.ApplyImpulse(settings.useCardEffectValues
                    ?ExpeditionEffectRules.RadialImpulse(point,playerCentre,impulse)
                    :Vector3.up*(settings.windLift+(copy.Level-1)*1.6f));
            var bodies=new HashSet<Rigidbody>();
            foreach(var col in Physics.OverlapSphere(point,radius,~0,QueryTriggerInteraction.Ignore))
            {
                var body=col.attachedRigidbody;
                if(!body || body.isKinematic || !bodies.Add(body) || body.GetComponent<CardProjectile>())continue;
                Vector3 force=settings.useCardEffectValues
                    ?ExpeditionEffectRules.RadialImpulse(point,body.worldCenterOfMass,impulse)
                    :((body.worldCenterOfMass-point).normalized+Vector3.up*.8f)*settings.windImpulse*copy.Level;
                // ForceMode.Impulse applies Δv = I / Rigidbody.mass exactly once per body.
                body.AddForce(force,ForceMode.Impulse);
                var affected=body.GetComponent<WorldCreature>();if(affected)affected.Stagger(.55f);
            }
        }
        public void ReturnToShop(bool defeated)
        {
            if(!Running)return;
            if(!defeated && !workshop.Model.Current.ReturnHome())return;
            if(defeated)workshop.Model.Current.Defeat();
            Returning=true;thrower.Cancel();control.SetBlocked(this,true);
            StartCoroutine(ReturnTransition(defeated));
        }
        IEnumerator ReturnTransition(bool defeated)
        {
            if(!defeated) {
                var home=CardCopyForReturn();
                Launch(home,viewCamera.transform.position+viewCamera.transform.forward*.6f,viewCamera.transform.forward*9+Vector3.up*3,0,true);
            }
            hand.Hide();PlaySound(settings.returnSound);
            float time=0;while(time<settings.returnSeconds){time+=Time.unscaledDeltaTime;hud.SetFade(time/settings.returnSeconds);yield return null;}
            int kept=0,lost=0;var retained=new List<LootSave>();
            foreach(var entry in loot) {
                int count=defeated?Mathf.FloorToInt(entry.count*(1-settings.defeatLootLoss)):entry.count;
                kept+=count;lost+=entry.count-count;
                if(count>0)retained.Add(new LootSave{id=entry.id,name=entry.name,count=count,value=entry.value});
            }
            motor.Teleport(shopPose.position,shopPose.rotation);vitals.Restore();
            control.SetBlocked(this,false);hud.canvas.enabled=false;hud.SetFade(0);Returning=false;
            // Complete only after local cleanup: the integration callback may disable this world root.
            workshop.CompleteOuting(defeated?$"Вы вернулись после поражения. Сохранено: {kept}; потеряно: {lost}.":$"Вы дома. В запас лавки добавлено: {kept}. Карты снова доступны.",retained,defeated);
        }
        CardCopy CardCopyForReturn()=>workshop.Model.Current.Get(DeckKind.Exploration).Hand.First(c=>c.IsReturn);
    }
}
