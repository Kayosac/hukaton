using System;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Hakaton.Expeditions.Events
{
    /// <summary>
    /// «Звериная разборка»: two packs of different beasts at each other's throats. The player can wait it out and pick
    /// up what the fallen drop, finish off the wounded winners, or set the packs on each other himself.
    /// <para>
    /// <see cref="Variant.AlreadyFighting"/>: the packs posture at each other and the fight starts the first time the
    /// player comes within <see cref="startDistance"/>, so that he sees it. <see cref="Variant.Calm"/>: two calm packs
    /// stand a little apart and do not see each other; they fight when they come close (a «Манок» between them, an
    /// «Притяжение» pulling them together, a shove of one into the other), or when the player hits a beast of one pack
    /// so that the other pack gets the blame (the hit came from its side).
    /// </para>
    /// <para>
    /// One prefab with no scene references: the beasts are spawned from the pairs' prefabs as the endless world spawns
    /// its creatures (<see cref="WorldCreature.world"/>, <see cref="ExpeditionWorld.creatures"/>), on the ground and the
    /// navigation below the event. The whole fight stays within <see cref="arenaRadius"/> of the centre. During the
    /// fight the beasts ignore the player until he hurts one or comes within <see cref="noticeDistance"/>: that one goes
    /// for the player. The fallen drop their usual loot; the survivors go back to their ordinary behaviour, wounded.
    /// <see cref="Capture"/> / <see cref="Restore"/> keep the state across unloads (a fight cut short counts as over).
    /// </para>
    /// </summary>
    public sealed class BeastBrawl : MonoBehaviour, IOutingSpawned, IBrawlReferee
    {
        public enum Variant
        {
            [Tooltip("The packs are already at it: the fight starts when the player first comes near enough to see it.")]
            AlreadyFighting,
            [Tooltip("Two calm packs that do not see each other: the player sets them on each other.")]
            Calm,
        }

        public enum Phase { NotStarted, Fighting, Over }

        /// <summary>Which beasts fight which, and how many of each.</summary>
        [Serializable]
        public sealed class Pairing
        {
            [Tooltip("For the Inspector only.")]
            public string name;
            [Tooltip("The first pack's beast: a creature prefab from Assets/Game/Prefabs.")]
            public WorldCreature packA;
            [Tooltip("How many of the first pack: from and to, inclusive.")]
            public Vector2Int countA = new Vector2Int(2, 3);
            [Tooltip("The second pack's beast.")]
            public WorldCreature packB;
            public Vector2Int countB = new Vector2Int(2, 3);
            [Tooltip("Health of the brawlers of the first pack as a share of their usual health: tunes how long the fight lasts and who usually wins.")]
            [Range(.25f, 10)] public float healthA = 1;
            [Range(.25f, 10)] public float healthB = 1;

            public WorldCreature Prefab(int pack) => pack == 0 ? packA : packB;
            public Vector2Int Count(int pack) => pack == 0 ? countA : countB;
            public float Health(int pack) => pack == 0 ? healthA : healthB;
        }

        /// <summary>One beast in a saved state.</summary>
        [Serializable]
        public struct BeastSave
        {
            public int pack;
            public bool alive;
            public int health;
            public Vector3 position;
        }

        /// <summary>Everything needed to bring the event back after its part of the world was unloaded.</summary>
        [Serializable]
        public sealed class Save
        {
            public Phase phase;
            public int pairing;
            public BeastSave[] beasts = new BeastSave[0];
        }

        [Header("Event")]
        [Tooltip("Set by the world that spawns the event (or by ExpeditionWorld.SpawnForOuting).")]
        public ExpeditionWorld world;
        public Variant variant = Variant.AlreadyFighting;
        [Tooltip("Which pairing to use: an index in the list below, or -1 to pick one at random when it spawns.")]
        public int pairing = -1;
        public List<Pairing> pairings = new List<Pairing>();

        [Header("Place")]
        [Tooltip("The whole fight, chases included, stays within this distance of the centre, metres.")]
        [Min(2)] public float arenaRadius = 7;
        [Tooltip("Distance between the two packs' middles, metres: close for a fight under way, apart for calm packs.")]
        [Min(.5f)] public float fightingPackGap = 4.5f, calmPackGap = 7;
        [Tooltip("Pack members stand within this distance of their pack's middle, metres.")]
        [Min(0)] public float packSpread = 1.3f;
        [Tooltip("Calm pack members only wander this far from where they stand, metres (so the packs do not drift into each other by themselves).")]
        [Min(0)] public float calmWanderRadius = 1;

        [Header("Start")]
        [Tooltip("Already fighting: the fight starts the first time the player is this close to the centre, metres.")]
        [Min(0)] public float startDistance = 20;
        [Tooltip("Calm: beasts of the two packs closer than this go for each other, metres.")]
        [Min(0)] public float contactDistance = 2.2f;
        [Tooltip("Calm: the player's hit is blamed on the other pack when it came from that pack's side within this angle, degrees.")]
        [Range(0, 90)] public float blameAngle = 50;
        [Tooltip("The packs glare and growl at each other this long before the first blow, seconds.")]
        [Min(0)] public float standoffSeconds = 1.3f;

        [Header("Fight")]
        [Tooltip("A beast goes for the player when he comes closer than this during the fight, metres.")]
        [Min(0)] public float noticeDistance = 3;
        [Tooltip("How often a beast looks for the nearest rival, seconds.")]
        [Min(.1f)] public float retargetSeconds = .8f;
        [Tooltip("A fight that drags on is called off after this long; whoever is alive has won.")]
        [Min(5)] public float maximumSeconds = 45;

        [Header("Look")]
        [Tooltip("Flash where a beast's blow lands on another (Retro Arsenal).")]
        public VfxBurst hitFlash;
        [Min(0)] public float hitFlashScale = .8f;
        public Color hitFlashTint = Color.white;
        [Tooltip("Dust kicked up where the beasts are at each other (Retro Arsenal).")]
        public VfxBurst brawlDust;
        [Min(0)] public float dustScale = 1;
        [Min(.05f)] public float dustInterval = .4f;
        public Color dustTint = new Color(.86f, .78f, .64f);

        sealed class Member
        {
            public int pack;
            public WorldCreature creature;
            public bool againstPlayer;
            public float territory;
            public float nextRetarget;
            public bool Alive => creature && creature.gameObject.activeSelf && creature.Alive;
            public bool Brawler => Alive && !againstPlayer;
        }

        readonly List<Member> members = new List<Member>();
        bool spawned;
        float fightStarted, standoffUntil, nextDust;
        Vector3 centre;

        public Phase Current { get; private set; }
        /// <summary>The pairing in use, once spawned.</summary>
        public int PairingIndex { get; private set; } = -1;
        public bool Spawned => spawned;
        /// <summary>Where the fight happens: the event's spot on the ground.</summary>
        public Vector3 Centre => spawned ? centre : transform.position;
        /// <summary>Seconds since the first blow (or how long the fight lasted once over).</summary>
        public float FightSeconds { get; private set; }
        /// <summary>What set the packs on each other (for tests and tuning): player near, contact, blame, a bump, a blow, a call.</summary>
        public string StartedBy { get; private set; } = "";
        /// <summary>Raised when the event moves to another phase.</summary>
        public event Action<Phase> PhaseChanged;

        /// <summary>Every beast of the event, the fallen included (null once a fallen one was not brought back).</summary>
        public IEnumerable<WorldCreature> Beasts { get { foreach (var m in members) if (m.creature) yield return m.creature; } }
        /// <summary>Beasts of this pack (0 or 1).</summary>
        public IEnumerable<WorldCreature> Pack(int pack) { foreach (var m in members) if (m.pack == pack && m.creature) yield return m.creature; }
        public int AliveIn(int pack) { int n = 0; foreach (var m in members) if (m.pack == pack && m.Alive) n++; return n; }
        /// <summary>The pack of a beast of this event, or -1.</summary>
        public int PackOf(WorldCreature creature) { var m = Find(creature); return m != null ? m.pack : -1; }
        /// <summary>The beast left the fight to go for the player.</summary>
        public bool AgainstPlayer(WorldCreature creature) { var m = Find(creature); return m != null && m.againstPlayer; }

        Pairing Pair => PairingIndex >= 0 && PairingIndex < pairings.Count ? pairings[PairingIndex] : null;

        void Reset()
        {
            // Defaults of the brief: goblin dogs vs pumpkin rats, rooster vs pumpkin rats, bee swarm vs hostile bushes.
            pairings = new List<Pairing>
            {
                new Pairing { name = "Гоблин-псы против тыкв-крыс", countA = new Vector2Int(2, 2), countB = new Vector2Int(3, 4) },
                new Pairing { name = "Петух против тыкв-крыс", countA = new Vector2Int(2, 2), countB = new Vector2Int(3, 3) },
                new Pairing { name = "Пчелиный рой против враждебных кустов", countA = new Vector2Int(3, 4), countB = new Vector2Int(2, 2) },
            };
        }

        // ---------------- spawning ----------------

        void IOutingSpawned.OnSpawned(ExpeditionWorld outing)
        {
            world = outing;
            if (!spawned) Spawn(Random.Range(int.MinValue, int.MaxValue));
        }

        void Start()
        {
            if (!spawned && world) Spawn(Random.Range(int.MinValue, int.MaxValue));
        }

        /// <summary>Spawns a new event: picks the pairing (unless set) and the pack sizes from <paramref name="seed"/>.</summary>
        public void Spawn(int seed)
        {
            if (!world) { Debug.LogWarning("BeastBrawl: no world to spawn the beasts into.", this); return; }
            Clear();
            var random = new System.Random(seed);
            PairingIndex = ChoosePairing(random);
            var pair = Pair;
            if (pair == null || !pair.packA || !pair.packB) { Debug.LogWarning("BeastBrawl: the pairing has no beasts.", this); return; }
            centre = Ground(transform.position);
            spawned = true;
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            Vector3 axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            float gap = variant == Variant.Calm ? calmPackGap : fightingPackGap;
            for (int pack = 0; pack < 2; pack++)
            {
                Vector2Int range = pair.Count(pack);
                int count = Mathf.Clamp(range.x + random.Next(Mathf.Max(0, range.y - range.x) + 1), 1, 8);
                Vector3 middle = centre + axis * gap * .5f * (pack == 0 ? -1 : 1);
                for (int i = 0; i < count; i++)
                {
                    // A loose ring round the pack's middle, toward its own side.
                    float a = (float)(random.NextDouble() * Math.PI * 2);
                    float r = packSpread * Mathf.Sqrt((float)random.NextDouble());
                    Vector3 at = middle + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    Vector3 facing = (pack == 0 ? axis : -axis);
                    var creature = SpawnBeast(pack, Place(at), Quaternion.LookRotation(facing));
                    if (creature) creature.ResetForOuting(random);
                    Adopt(pack, creature);
                }
            }
            SetPhase(Phase.NotStarted);
            PostureIfAlreadyFighting();
        }

        int ChoosePairing(System.Random random)
        {
            if (pairings.Count == 0) return -1;
            if (pairing >= 0 && pairing < pairings.Count) return pairing;
            return random.Next(pairings.Count);
        }

        WorldCreature SpawnBeast(int pack, Vector3 at, Quaternion rotation)
        {
            var prefab = Pair.Prefab(pack);
            var creature = Instantiate(prefab, at, rotation, transform);
            creature.name = prefab.name;
            creature.world = world;
            creature.spawnPoints = null;
            // Brawl health for this event's beasts only (Awake already read the prefab's).
            creature.maximumHealth = Mathf.Max(1, Mathf.RoundToInt(prefab.maximumHealth * Pair.Health(pack)));
            return creature;
        }

        void Adopt(int pack, WorldCreature creature)
        {
            var member = new Member { pack = pack, creature = creature };
            members.Add(member);
            if (!creature) return;
            member.territory = creature.territoryRadius;
            if (variant == Variant.Calm) creature.territoryRadius = Mathf.Min(creature.territoryRadius, calmWanderRadius);
            creature.JoinBrawl(this);
            if (!world.creatures.Contains(creature)) world.creatures.Add(creature);
        }

        /// <summary>The state to keep while the event's part of the world is unloaded.</summary>
        public Save Capture()
        {
            var save = new Save { phase = Current, pairing = PairingIndex, beasts = new BeastSave[members.Count] };
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                bool alive = m.Alive;
                save.beasts[i] = new BeastSave
                {
                    pack = m.pack,
                    alive = alive,
                    health = alive ? m.creature.Health : 0,
                    position = alive ? m.creature.transform.position : Vector3.zero,
                };
            }
            return save;
        }

        /// <summary>
        /// Brings the event back as it was captured. A fight that was going on counts as over with the beasts that were
        /// alive: they come back wounded and behave as usual.
        /// </summary>
        public void Restore(Save save)
        {
            if (save == null) return;
            if (!world) { Debug.LogWarning("BeastBrawl: no world to restore the beasts into.", this); return; }
            Clear();
            PairingIndex = save.pairing >= 0 && save.pairing < pairings.Count ? save.pairing : ChoosePairing(new System.Random(0));
            if (Pair == null) return;
            centre = Ground(transform.position);
            spawned = true;
            var phase = save.phase == Phase.Fighting ? Phase.Over : save.phase;
            foreach (var beast in save.beasts ?? new BeastSave[0])
            {
                int pack = Mathf.Clamp(beast.pack, 0, 1);
                if (!beast.alive) { members.Add(new Member { pack = pack }); continue; }
                Vector3 toCentre = centre - beast.position; toCentre.y = 0;
                var creature = SpawnBeast(pack, beast.position, Quaternion.LookRotation(toCentre.sqrMagnitude > .01f ? toCentre : Vector3.forward));
                creature.Resume(beast.health, beast.position);
                Adopt(pack, creature);
            }
            FightSeconds = 0;
            SetPhase(phase);
            if (phase == Phase.Over) foreach (var m in members) Release(m);
            else PostureIfAlreadyFighting();
        }

        void Clear()
        {
            foreach (var m in members)
            {
                if (!m.creature) continue;
                if (world) world.creatures.Remove(m.creature);
                m.creature.gameObject.SetActive(false);
                Destroy(m.creature.gameObject);
            }
            members.Clear();
            spawned = false;
            PairingIndex = -1;
            FightSeconds = 0;
            StartedBy = "";
            Current = Phase.NotStarted;
        }

        void OnDestroy()
        {
            if (!world) return;
            foreach (var m in members) if (m.creature) world.creatures.Remove(m.creature);
        }

        // ---------------- running ----------------

        void Update()
        {
            if (!spawned || !world || !world.Running) return;
            switch (Current)
            {
                case Phase.NotStarted: WaitForStart(); break;
                case Phase.Fighting: RunFight(); break;
            }
        }

        void WaitForStart()
        {
            Vector3 player = world.Player.Position;
            if (variant == Variant.AlreadyFighting)
            {
                if (Flat(player - centre).magnitude < startDistance) { Begin(.2f, "player in sight"); return; }
                // Walking right up to the posturing beasts: the nearest one turns on the intruder.
                foreach (var m in members)
                    if (m.Brawler && Flat(player - m.creature.Position).magnitude < noticeDistance) { TurnOnPlayer(m); Begin(.2f, "player walked up"); return; }
                return;
            }
            // Calm packs: two beasts of different packs that come close (lured, pulled, shoved) go for each other.
            for (int i = 0; i < members.Count; i++)
            {
                var a = members[i];
                if (!a.Brawler) continue;
                for (int j = i + 1; j < members.Count; j++)
                {
                    var b = members[j];
                    if (b.pack == a.pack || !b.Brawler) continue;
                    if (Flat(a.creature.Position - b.creature.Position).magnitude < contactDistance) { Begin(standoffSeconds, "contact"); return; }
                }
            }
        }

        /// <summary>The packs go for each other: a standoff of glares and growls, then the first blows.</summary>
        public void Begin() => Begin(standoffSeconds, "call");

        void Begin(float standoff, string reason)
        {
            if (Current != Phase.NotStarted || !spawned) return;
            StartedBy = reason;
            fightStarted = Time.time;
            standoffUntil = Time.time + standoff;
            SetPhase(Phase.Fighting);
            // A beast that wandered off after the player (calm packs see him as usual) stays out of it.
            foreach (var m in members)
                if (m.Brawler && Flat(m.creature.Position - centre).magnitude > arenaRadius) m.againstPlayer = true;
            foreach (var m in members)
            {
                if (!m.Brawler) continue;
                m.creature.territoryRadius = m.territory;
                var rival = NearestRival(m);
                if (rival == null) continue;
                if (standoff > 0) m.creature.Posture(rival.creature, centre, arenaRadius);
                else m.creature.Fight(rival.creature, centre, arenaRadius);
                m.nextRetarget = Time.time + retargetSeconds;
            }
        }

        void RunFight()
        {
            FightSeconds = Time.time - fightStarted;
            Vector3 player = world.Player.Position;
            bool standoff = Time.time < standoffUntil;
            foreach (var m in members)
            {
                if (!m.Brawler) continue;
                if (Flat(player - m.creature.Position).magnitude < noticeDistance) { TurnOnPlayer(m); continue; }
                bool holding = m.creature.Posturing;
                var current = m.creature.Target as WorldCreature;
                bool lost = !current || !current.Alive || AgainstPlayer(current) || PackOf(current) < 0;
                if (!lost && holding == standoff && Time.time < m.nextRetarget) continue;
                m.nextRetarget = Time.time + retargetSeconds * Random.Range(.8f, 1.2f);
                var rival = NearestRival(m);
                if (rival == null) continue;
                // A little loyal to the one it is fighting: only a clearly nearer rival takes over.
                if (!lost && rival.creature != current
                    && Flat(rival.creature.Position - m.creature.Position).magnitude + 1.5f > Flat(current.Position - m.creature.Position).magnitude)
                    rival = Find(current);
                if (standoff) m.creature.Posture(rival.creature, centre, arenaRadius);
                else m.creature.Fight(rival.creature, centre, arenaRadius);
            }
            if (!standoff) KickDust();
            if (AliveBrawlers(0) == 0 || AliveBrawlers(1) == 0 || FightSeconds > maximumSeconds) End();
        }

        int AliveBrawlers(int pack) { int n = 0; foreach (var m in members) if (m.pack == pack && m.Brawler) n++; return n; }

        void End()
        {
            FightSeconds = Time.time - fightStarted;
            SetPhase(Phase.Over);
            foreach (var m in members) Release(m);
        }

        void Release(Member m)
        {
            if (!m.creature) return;
            m.creature.LeaveBrawl();
            m.creature.territoryRadius = m.territory > 0 ? m.territory : m.creature.territoryRadius;
        }

        void TurnOnPlayer(Member m)
        {
            if (m.againstPlayer || !m.Alive) return;
            m.againstPlayer = true;
            m.creature.territoryRadius = m.territory;
            m.creature.FightPlayer();
        }

        void PostureIfAlreadyFighting()
        {
            if (variant != Variant.AlreadyFighting || Current != Phase.NotStarted) return;
            foreach (var m in members)
            {
                if (!m.Brawler) continue;
                var rival = NearestRival(m);
                if (rival != null) m.creature.Posture(rival.creature, centre, arenaRadius);
            }
        }

        void SetPhase(Phase phase)
        {
            if (Current == phase && phase != Phase.NotStarted) return;
            Current = phase;
            PhaseChanged?.Invoke(phase);
        }

        void KickDust()
        {
            if (!brawlDust || Time.time < nextDust) return;
            nextDust = Time.time + dustInterval * Random.Range(.7f, 1.3f);
            // Somewhere two rivals are at each other.
            Member a = null, b = null;
            float best = 2.6f;
            foreach (var m in members)
            {
                if (!m.Brawler || !(m.creature.Target is WorldCreature rival) || !rival.Alive) continue;
                float d = Flat(rival.Position - m.creature.Position).magnitude;
                if (d < best && Random.value < .6f) { best = d; a = m; b = Find(rival); }
            }
            if (a == null || b == null) return;
            Vector3 at = Vector3.Lerp(a.creature.Position, b.creature.Position, Random.Range(.3f, .7f)) + Vector3.up * .2f;
            world.Spawn(brawlDust, at, Quaternion.identity, dustTint, dustScale * Random.Range(.8f, 1.25f));
        }

        // ---------------- referee ----------------

        void IBrawlReferee.CreatureHurt(WorldCreature creature, BrawlHurtSource source, ICombatant by, Vector3 impulse)
        {
            var m = Find(creature);
            if (m == null) return;
            if (source == BrawlHurtSource.Creature)
            {
                if (hitFlash && Find(by as WorldCreature) != null)
                    world.Spawn(hitFlash, creature.Centre, Quaternion.identity, hitFlashTint, hitFlashScale);
                if (Current == Phase.NotStarted && by is WorldCreature attacker && PackOf(attacker) >= 0 && PackOf(attacker) != m.pack) Begin(standoffSeconds * .5f, "blow");
                return;
            }
            if (source != BrawlHurtSource.Player) return;
            switch (Current)
            {
                case Phase.NotStarted:
                    if (variant == Variant.AlreadyFighting) { TurnOnPlayer(m); Begin(.2f, "player hit"); }
                    else if (Blamed(m, impulse)) Begin(standoffSeconds, "blame");
                    // otherwise it is the player's fault and the beast behaves as usual (it goes for him)
                    break;
                case Phase.Fighting:
                    TurnOnPlayer(m);
                    break;
            }
        }

        void IBrawlReferee.CreatureBumped(WorldCreature creature, WorldCreature other)
        {
            // Posturing packs stand close and jostle; only calm packs are set off by a bump.
            if (Current != Phase.NotStarted || variant != Variant.Calm) return;
            var a = Find(creature);
            var b = Find(other);
            if (a == null || b == null || a.pack == b.pack || !a.Brawler || !b.Brawler) return;
            Begin(standoffSeconds, "bump");
        }

        /// <summary>
        /// The hit came from the other pack's side: the nearest beast of that pack stands between the hurt one and the
        /// player, within <see cref="blameAngle"/> of the direction the hit came from.
        /// </summary>
        bool Blamed(Member hurt, Vector3 impulse)
        {
            var rival = NearestRival(hurt);
            if (rival == null) return false;
            Vector3 at = hurt.creature.Position;
            Vector3 toPlayer = Flat(world.Player.Position - at);
            Vector3 from = Flat(-impulse);
            if (from.sqrMagnitude < 1e-4f) from = toPlayer;
            if (from.sqrMagnitude < 1e-4f) return false;
            Vector3 toRival = Flat(rival.creature.Position - at);
            return toRival.magnitude < toPlayer.magnitude && Vector3.Angle(from, toRival) <= blameAngle;
        }

        Member NearestRival(Member of)
        {
            Member best = null;
            float nearest = float.MaxValue;
            foreach (var m in members)
            {
                if (m.pack == of.pack || !m.Brawler) continue;
                float d = Flat(m.creature.Position - of.creature.Position).sqrMagnitude;
                if (d < nearest) { nearest = d; best = m; }
            }
            return best;
        }

        Member Find(WorldCreature creature)
        {
            if (!creature) return null;
            foreach (var m in members) if (m.creature == creature) return m;
            return null;
        }

        // ---------------- ground ----------------

        static readonly RaycastHit[] groundHits = new RaycastHit[8];

        /// <summary>The ground under a point (ignoring the event's own beasts), on the navigation when there is some near.</summary>
        Vector3 Ground(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 6, Vector3.down, groundHits, 14, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody && (!hit.rigidbody.isKinematic || hit.rigidbody.GetComponent<WorldCreature>())) continue;
                if (hit.distance < best) { best = hit.distance; point.y = hit.point.y; }
            }
            if (NavMesh.SamplePosition(point, out var nav, 1.5f, NavMesh.AllAreas)) point = nav.position;
            return point;
        }

        /// <summary>A spawn spot: on the ground, on the navigation, and inside the circle.</summary>
        Vector3 Place(Vector3 at)
        {
            Vector3 offset = Flat(at - centre);
            float limit = arenaRadius - 1.5f;
            if (offset.magnitude > limit) at = centre + offset.normalized * limit;
            return Ground(at);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, .35f, .25f, .8f);
            Vector3 c = Centre;
            Vector3 previous = c + Vector3.forward * arenaRadius;
            for (int i = 1; i <= 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2;
                Vector3 next = c + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * arenaRadius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif
    }
}
