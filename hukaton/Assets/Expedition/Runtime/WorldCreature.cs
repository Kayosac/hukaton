using UnityEngine;
using UnityEngine.AI;
namespace Hakaton.Expeditions
{
    public enum Temperament { Timid, Territorial, Aggressive }
    public enum CreatureState { Roam, Warning, Chase, Search, Return, Windup, Strike, Recovery, Frozen, Down, Dead }
    public sealed class WorldCreature:MonoBehaviour
    {
        public ExpeditionWorld world;
        public Temperament temperament;
        public string displayName;
        public Rigidbody body;
        public NavMeshAgent navigator;
        public Transform visual,head;
        public Renderer[] skin;
        public WorldLoot trophyPrefab;
        public int maximumHealth=8,attackHalfHearts=2;
        public float sightRange=15,leash=23,speed=3.5f,attackRange=2.4f,contactRange=1.6f,windup=.7f,strikeSeconds=.45f,recovery=1.1f;
        public bool dash;
        public int Health{get;private set;}
        public CreatureState State{get;private set;}
        public bool Alive=>Health>0;
        Vector3 home,lastSeen,strikeDirection;
        Quaternion homeRotation;
        float timer,unseen,frozenUntil,downUntil,burnUntil,curseUntil,nextDot,roamAt;
        int curseDamage;
        bool hitDuringStrike;
        MaterialPropertyBlock colours; Color[] baseColours;
        void Awake()
        {
            // These nonserialized navigation switches leave physical position/rotation to the Rigidbody.
            navigator.updatePosition=false;navigator.updateRotation=false;
            home=transform.position;homeRotation=transform.rotation;colours=new MaterialPropertyBlock();Health=maximumHealth;
            baseColours=System.Array.ConvertAll(skin,r=>r.sharedMaterial.color);
        }
        public void ResetForOuting()
        {
            gameObject.SetActive(true);Health=maximumHealth;State=CreatureState.Roam;
            frozenUntil=downUntil=burnUntil=curseUntil=nextDot=unseen=0;timer=0;
            body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.position=home;body.rotation=homeRotation;
            if(navigator.enabled && navigator.isOnNavMesh){navigator.Warp(home);navigator.ResetPath();}
            visual.localRotation=Quaternion.identity;Tint(Color.white);
        }
        bool SeesPlayer()
        {
            Vector3 eye=body.position+Vector3.up*.9f,to=world.motor.transform.position+Vector3.up*.8f-eye;
            if(to.magnitude>sightRange)return false;
            if(State==CreatureState.Roam && Vector3.Dot(transform.forward,to.normalized)<-.2f)return false;
            return !Physics.Raycast(eye,to.normalized,out var hit,to.magnitude,~0,QueryTriggerInteraction.Ignore)
                || hit.collider==world.motor.characterController;
        }
        void Update()
        {
            if(!world || !world.Running || !Alive)return;
            if(Time.time>nextDot && (Time.time<burnUntil || Time.time<curseUntil)) {
                nextDot=Time.time+1;Damage(Time.time<curseUntil?curseDamage:1,Vector3.zero,0);
                if(!Alive)return;
            }
            if(Time.time<frozenUntil){State=CreatureState.Frozen;Tint(new Color(.25f,.8f,1));return;}
            if(Time.time<downUntil){State=CreatureState.Down;return;}
            if(State==CreatureState.Frozen || State==CreatureState.Down){State=CreatureState.Recovery;timer=.5f;}
            Tint(Time.time<curseUntil?new Color(.7f,.35f,1):Time.time<burnUntil?new Color(1,.5f,.2f):Color.white);
            timer-=Time.deltaTime;
            Vector3 toPlayer=world.motor.transform.position-body.position;
            bool sees=SeesPlayer();if(sees){lastSeen=world.motor.transform.position;unseen=0;}else unseen+=Time.deltaTime;
            if(State==CreatureState.Windup) {
                Face(toPlayer);if(timer<=0){State=CreatureState.Strike;timer=strikeSeconds;strikeDirection=transform.forward;hitDuringStrike=false;world.PlaySound(world.settings.creatureSound);}return;
            }
            if(State==CreatureState.Strike) {
                if(!hitDuringStrike && toPlayer.magnitude<contactRange && Vector3.Dot(strikeDirection,toPlayer.normalized)>.35f){world.vitals.Hurt(attackHalfHearts,strikeDirection*5);hitDuringStrike=true;}
                if(timer<=0){State=CreatureState.Recovery;timer=recovery;}return;
            }
            if(State==CreatureState.Recovery){if(timer<=0)State=CreatureState.Chase;return;}
            if(temperament==Temperament.Timid && sees){State=CreatureState.Chase;Go(body.position-toPlayer.normalized*7);return;}
            if(temperament==Temperament.Timid && (State==CreatureState.Chase || State==CreatureState.Search))State=CreatureState.Return;
            if((body.position-home).magnitude>leash || unseen>4 && (State==CreatureState.Search || State==CreatureState.Chase))State=CreatureState.Return;
            if(State==CreatureState.Return){Go(home);if((body.position-home).magnitude<1)State=CreatureState.Roam;return;}
            if(sees && State==CreatureState.Roam){State=temperament==Temperament.Territorial?CreatureState.Warning:CreatureState.Chase;timer=1.2f;}
            if(State==CreatureState.Warning){Face(toPlayer);if(!sees)State=CreatureState.Roam;else if(timer<=0)State=CreatureState.Chase;return;}
            if(State==CreatureState.Chase || State==CreatureState.Search) {
                State=sees?CreatureState.Chase:CreatureState.Search;Go(lastSeen);
                if(sees && toPlayer.magnitude<(dash?attackRange:contactRange*.95f)){State=CreatureState.Windup;timer=windup;}
            } else if(Time.time>roamAt){roamAt=Time.time+5;Vector2 random=Random.insideUnitCircle*4;Go(home+new Vector3(random.x,0,random.y));}
        }
        void FixedUpdate()
        {
            if(!world || !world.Running || !Alive)return;
            if(navigator.enabled && navigator.isOnNavMesh)navigator.nextPosition=body.position;
            if(State==CreatureState.Down)return;
            Vector3 desired=Vector3.zero;
            if(State==CreatureState.Strike && dash)desired=strikeDirection*speed*3;
            else if(State==CreatureState.Chase || State==CreatureState.Roam || State==CreatureState.Search || State==CreatureState.Return)
                if(navigator.enabled && navigator.isOnNavMesh)desired=navigator.desiredVelocity.normalized*speed*(State==CreatureState.Roam ? .4f : 1);
            float acceleration=State==CreatureState.Strike && dash?90:16;
            Vector3 velocity=body.linearVelocity;velocity.x=Mathf.MoveTowards(velocity.x,desired.x,Time.fixedDeltaTime*acceleration);velocity.z=Mathf.MoveTowards(velocity.z,desired.z,Time.fixedDeltaTime*acceleration);body.linearVelocity=velocity;
            if(desired.sqrMagnitude>.1f && State!=CreatureState.Strike)Face(desired);
            float bounce=desired.sqrMagnitude>1?Mathf.Abs(Mathf.Sin(Time.time*12))*.07f:Mathf.Sin(Time.time*2)*.015f;
            visual.localPosition=new Vector3(0,bounce,0);head.localRotation=Quaternion.Euler(State==CreatureState.Windup?-22:0,0,0);
        }
        void Go(Vector3 point){if(navigator.enabled && navigator.isOnNavMesh && NavMesh.SamplePosition(point,out var hit,4,NavMesh.AllAreas))navigator.SetDestination(hit.position);}
        void Face(Vector3 direction){direction.y=0;if(direction.sqrMagnitude>.01f)body.MoveRotation(Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(direction),Time.deltaTime*200));}
        void Tint(Color tint){for(int i=0;i<skin.Length;i++){colours.SetColor("_BaseColor",baseColours[i]*tint);skin[i].SetPropertyBlock(colours);}}
        public void Freeze(float seconds){if(!Alive)return;frozenUntil=Time.time+seconds;State=CreatureState.Frozen;body.linearVelocity=Vector3.zero;}
        public void Stagger(float seconds){if(!Alive)return;downUntil=Time.time+seconds;State=CreatureState.Down;}
        public void Curse(float seconds,int damage){if(!Alive)return;curseUntil=Time.time+seconds;curseDamage=damage;nextDot=Time.time+.3f;Alert();}
        public void Damage(int amount,Vector3 impulse,float burnSeconds)
        {
            if(!Alive)return;Health=Mathf.Max(0,Health-amount);body.AddForce(impulse,ForceMode.Impulse);
            if(burnSeconds>0)burnUntil=Time.time+burnSeconds;
            if(impulse.magnitude>5)Stagger(.6f);Alert();
            if(Health>0)return;
            State=CreatureState.Dead;
            var trophy=Instantiate(trophyPrefab,body.position+Vector3.up*.7f,Quaternion.identity,world.spawnedObjects);trophy.world=world;
            world.Announce(displayName+": трофей можно подобрать");gameObject.SetActive(false);
        }
        void Alert(){if(State==CreatureState.Roam || State==CreatureState.Warning){State=CreatureState.Chase;lastSeen=world.motor.transform.position;}}
        void OnCollisionEnter(Collision collision){if(world && world.Running && Alive && collision.relativeVelocity.magnitude>10)Damage(Mathf.CeilToInt((collision.relativeVelocity.magnitude-9)*.7f),Vector3.zero,0);}
    }
}
