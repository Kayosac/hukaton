using System.Linq;
using Hakaton.Decks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace Hakaton.Expeditions
{
    public sealed class ExpeditionHud:MonoBehaviour
    {
        public ExpeditionWorld world;
        public Canvas canvas;
        public TMP_Text deck,selected,hearts,notice,interaction,inventory,chargeLabel,target;
        public Image chargeFill,fade,hurt;
        public GameObject chargePanel,inventoryPanel;
        public Image[] heartFills;
        public Image staminaFill;
        public GameObject staminaPanel;
        public void SetFade(float amount){fade.color=new Color(.035f,.055f,.05f,Mathf.Clamp01(amount));}
        void LateUpdate()
        {
            if(!world.Running)return;
            var trip=world.workshop.Model.Current;var active=trip.Active;
            deck.text=(trip.ActiveKind==DeckKind.Exploration?"ПУТЬ":"БОЙ")+"  /  "+active.Hand.Count+" в руке  ·  "+active.StockCount+" в колоде";
            var card=world.thrower.Selected;
            selected.text=card==null?"Карты закончились":card.IsReturn?"Домой — бросьте карту возвращения":world.workshop.config.Find(card.Spec.Id).displayName+"  /  ур. "+card.Level;
            hearts.text=string.Concat(Enumerable.Range(0,world.settings.maximumHalfHearts/2).Select(i=>world.vitals.HalfHearts>=i*2+2?"[+] ":world.vitals.HalfHearts==i*2+1?"[/] ":"[ ] "))+(world.vitals.Armour>0?"  +"+world.vitals.Armour:"");
            if(heartFills!=null && heartFills.Length>0){hearts.text=world.vitals.Armour>0?"Защита "+world.vitals.Armour:"";for(int i=0;i<heartFills.Length;i++)heartFills[i].fillAmount=Mathf.Clamp01((world.vitals.HalfHearts-i*2)/2f);}
            if(staminaPanel){staminaPanel.SetActive(world.motor.stamina.Normalized<.99f);staminaFill.fillAmount=world.motor.stamina.Normalized;}
            notice.text=Time.time<world.NoticeUntil?world.Notice:"";
            interaction.text=world.FocusedLoot?"E  Взять: "+world.FocusedLoot.displayName:"";
            bool prep=world.thrower.Preparation.Active;chargePanel.SetActive(prep);
            if(prep){chargeFill.fillAmount=world.thrower.Preparation.Charge;chargeLabel.text="БРОСОК "+Mathf.RoundToInt(world.thrower.Preparation.Charge*100)+"%   /   "+(world.thrower.Preparation.GestureUsed?"ПОДКРУТКА ":"Shift: подкрутка ")+Mathf.RoundToInt(world.thrower.Preparation.Spin*100)+"%";}
            bool bag=Keyboard.current!=null && Keyboard.current.iKey.isPressed;inventoryPanel.SetActive(bag);
            if(bag)inventory.text=world.Loot.Count==0?"Сумка пуста":string.Join("\n",world.Loot.Select(x=>x.name+"  ×"+x.count));
            hurt.color=new Color(.7f,.1f,.05f,Mathf.Clamp01(1-(Time.time-world.vitals.LastChangeTime)/.3f)*.18f);
            target.text="";
            if(Physics.Raycast(world.viewCamera.transform.position,world.viewCamera.transform.forward,out var hit,24,~(1<<2),QueryTriggerInteraction.Ignore)) {
                var creature=hit.collider.GetComponentInParent<WorldCreature>();
                if(creature && creature.Alive)target.text=creature.displayName+"  "+creature.Health+" / "+creature.maximumHealth+(creature.State==CreatureState.Windup?"  !":"");
            }
        }
    }
}
