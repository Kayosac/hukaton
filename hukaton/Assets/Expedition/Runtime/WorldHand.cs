using UnityEngine;
namespace Hakaton.Expeditions
{
    public sealed class WorldHand:MonoBehaviour
    {
        public ExpeditionWorld world;
        public SpriteRenderer[] cards;
        public Transform glove;
        float dealtAt;
        public void Refresh()
        {
            dealtAt=Time.time;if(!world.Running){Hide();return;}
            var hand=world.workshop.Model.Current.Active.Hand;
            for(int i=0;i<cards.Length;i++) {
                cards[i].gameObject.SetActive(i<hand.Count);
                if(i<hand.Count)cards[i].sprite=hand[i].IsReturn?world.workshop.config.returnArtwork:world.workshop.config.Find(hand[i].Spec.Id).artwork;
            }
            glove.gameObject.SetActive(hand.Count>0);
        }
        public void Hide(){foreach(var card in cards)card.gameObject.SetActive(false);glove.gameObject.SetActive(false);}
        void LateUpdate()
        {
            if(!world.Running)return;
            var hand=world.workshop.Model.Current.Active.Hand;
            float deal=Mathf.Clamp01((Time.time-dealtAt)/.22f);
            for(int i=0;i<hand.Count;i++) {
                bool selected=world.thrower.Selected?.Id==hand[i].Id;
                float spread=(i-(hand.Count-1)*.5f);
                Vector3 target=new Vector3(-.30f+spread*.075f,-.29f+(selected ? .055f : 0)-(1-deal)*.16f,.65f+i*.002f);
                if(selected && world.thrower.Preparation.Active)target+=new Vector3(.09f,.04f,-.06f)*world.thrower.Preparation.Charge;
                cards[i].transform.localPosition=Vector3.Lerp(cards[i].transform.localPosition,target,Time.deltaTime*18);
                cards[i].transform.localRotation=Quaternion.Euler(4,0,-spread*8);
                cards[i].sortingOrder=selected?20:10+i;
            }
        }
    }
}
