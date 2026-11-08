using UnityEngine;
namespace Hakaton.Expeditions
{
    public sealed class WorldLoot:MonoBehaviour
    {
        public string itemId;
        public string displayName="Лесная находка";
        public int value=12;
        public bool guaranteed=true;
        [Range(0,1)] public float spawnChance=.7f;
        public Rigidbody body;
        public Transform visual;
        public ExpeditionWorld world;
        [Tooltip("Optional authored alternatives for this finding. Every selected position is saved in the scene.")]
        public Transform[] spawnPoints;
        Vector3 origin;Quaternion rotation;
        bool recorded;
        public bool Taken{get;private set;}
        void Awake(){Record();}
        void Record(){if(recorded)return;origin=transform.position;rotation=transform.rotation;recorded=true;}
        public void ResetForOuting(System.Random random)
        {
            Record();Taken=false;
            var point=spawnPoints!=null && spawnPoints.Length>0?spawnPoints[random.Next(spawnPoints.Length)]:null;
            transform.SetPositionAndRotation(point?point.position:origin,point?point.rotation:rotation);
            if(body){body.position=transform.position;body.rotation=transform.rotation;}
            if(body){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            gameObject.SetActive(guaranteed || random.NextDouble()<spawnChance);
        }
        public bool PickUp()
        {
            if(Taken || !world.Running)return false;
            Taken=true;world.Collect(itemId,displayName,value);gameObject.SetActive(false);return true;
        }
        void Update(){if(visual)visual.localRotation=Quaternion.Euler(0,Time.time*28,0);}
    }
}
