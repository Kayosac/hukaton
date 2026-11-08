using Hakaton.Decks.Unity;
using UnityEngine;
namespace Hakaton.Expeditions
{
    public enum ObstacleKind{Thorns,Lake}
    public sealed class WorldObstacle:MonoBehaviour
    {
        public ObstacleKind kind;
        public Collider barrier;
        public GameObject intact,changed;
        public ExpeditionWorld world;
        public bool Resolved{get;private set;}
        float nextHazard;
        public void ResetForOuting(){Resolved=false;intact.SetActive(true);changed.SetActive(false);barrier.enabled=true;}
        public bool Apply(CardAction action)
        {
            if(Resolved)return false;
            if(kind==ObstacleKind.Thorns && action==CardAction.Fire){Resolved=true;intact.SetActive(false);changed.SetActive(true);barrier.enabled=false;}
            if(kind==ObstacleKind.Lake && action==CardAction.Freeze){Resolved=true;intact.SetActive(false);changed.SetActive(true);barrier.enabled=false;}
            if(Resolved)world.Announce(kind==ObstacleKind.Lake?"Лёд выдержит до конца вылазки":"Заросли выгорели. Путь открыт");
            return Resolved;
        }
        void OnTriggerStay(Collider other)
        {
            if(kind!=ObstacleKind.Lake || Resolved || Time.time<nextHazard || !world.Running)return;
            if(other.GetComponentInParent<PlayerVitals>()!=world.vitals)return;
            nextHazard=Time.time+1.2f;world.vitals.Hurt(1,Vector3.up*4);
            world.Announce("Глубокая вода. Заморозьте поверхность");
        }
    }
}
