using System;
using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>What appears in the world: loot by item role and creatures by role.</summary>
    [CreateAssetMenu(menuName = "Hakaton/World Assembly/World content", fileName = "World content")]
    public sealed class WorldContent : ScriptableObject
    {
        [Serializable]
        public struct LootEntry
        {
            [Tooltip("find, find-hard, crystal")]
            public string id;
            public WorldLoot prefab;
        }

        [Serializable]
        public struct CreatureEntry
        {
            [Tooltip("Creature id the world plan names: bee-swarm, goblin-dog, … (see CreatureThemes)")]
            public string role;
            public WorldCreature[] prefabs;
        }

        public List<LootEntry> loot = new List<LootEntry>();
        public List<CreatureEntry> creatures = new List<CreatureEntry>();
        [Tooltip("Looping sparkle over a hard-won find (landmark rewards, card places).")]
        public GameObject hardGlow;
        [Tooltip("Looping sparkle over a crystal.")]
        public GameObject crystalGlow;

        [Tooltip("Ground of the roads between landmarks (the world's path ground).")]
        public Material roadMaterial;
        [Tooltip("Played once when a lookout shows the far landmarks.")]
        public Hakaton.Feel.SfxCue lookoutCue;

        [Header("Chests")]
        [Tooltip("Treasure chests of the endless world by ring (0 centre, 1 middle, 2 far): the chance a chunk has one and its tier. Richer further out.")]
        public ChestSpawnTable[] chestTables = new ChestSpawnTable[3];

        [Header("Events")]
        [Tooltip("«Звериная разборка»: two packs at each other's throats (WorldEvents.Brawl).")]
        public Hakaton.Expeditions.Events.BeastBrawl brawl;
        [Tooltip("«Странное»: a ringing payphone and a hissing television (WorldEvents.Payphone / Television).")]
        public Oddity payphone, television;

        [Header("Weather")]
        [Tooltip("The rain fronts heard round the player (flat loops): rain, and the downpour of a thunderstorm.")]
        public Hakaton.Feel.SfxCue rainLoop, downpourLoop;
        [Tooltip("Thunder after a lightning strike, later the further it struck.")]
        public Hakaton.Feel.SfxCue thunder;
        [Tooltip("«Мокрый» under the hearts while the player is wet (the «Дождевая туча» card's badge).")]
        public PlayerStatusBadge wetBadge;
        [Tooltip("Lightning out of the thunder clouds: the «Разряд молнии» arc, drawn big.")]
        public LightningArc lightning;
        [Tooltip("Clouds over the rain fronts: their shape and material (Hakaton/World Cloud: seen far through the fog, darker in a storm).")]
        public Mesh cloudMesh;
        public Material cloudMaterial;
        [Tooltip("Rain hanging from the clouds (Hakaton/World Rain Veil).")]
        public Material rainVeil;

        [Header("Rotten swamp")]
        [Tooltip("Natural tar pools (SwampTarSpot in swamp chunks): the «Лужа смолы» card's puddle, with these numbers.")]
        public TarPuddleZone tarPuddle;
        [Tooltip("Natural tar: walking speed on it (times), burn time once lit (s), burn damage (units per second; 1 = ½ heart).")]
        [Min(0)] public float tarSlow = .5f, tarBurnSeconds = 30, tarBurnDamage = 1;

        [Header("Swamp life (SwampLife, WillOWisp)")]
        [Tooltip("The banks of ground fog that wander over the swamp (Hakaton/Ground Fog).")]
        public Material groundFog;
        [Tooltip("A fish's splash and a frog's plop.")]
        public Hakaton.Feel.VfxBurst fishSplash;
        public Hakaton.Feel.SfxCue fishCue, plopCue, frogCue;
        [Tooltip("Particle clouds that hang round the camera in the swamp.")]
        public GameObject dragonflies, midges;
        [Tooltip("A will-o'-the-wisp: leads to treasure or into a bog.")]
        public WillOWisp wisp;
        [Tooltip("The swamp's own look (colour grading, vignette), weighted by how much swamp is round the player.")]
        public UnityEngine.Rendering.VolumeProfile swampLook;

        [Header("Sunken chests")]
        [Tooltip("Per ring (1 centre, 2 middle, 3 far; the last one goes on): the chance that a chunk's deep water hides a " +
                 "chest on its bed, and which tier it is. Richer than the ring's land chests, but no legendary one in the " +
                 "centre and a very rare one in the middle (Game/Data/Chests/Chests sunken ring …).")]
        public ChestSpawnTable[] sunkenTables = new ChestSpawnTable[3];
        [Tooltip("A sunken chest pays this many times its tier's coins.")]
        [Min(1)] public float sunkenRiches = 3;
        [Tooltip("Bubbles rising from a sunken chest.")]
        public GameObject sunkenBubbles;

        /// <summary>The sunken chests' table of a ring (clamped to the tables there are); null when none is set.</summary>
        public ChestSpawnTable SunkenTable(int ring)
        {
            if (sunkenTables == null || sunkenTables.Length == 0) return null;
            return sunkenTables[Mathf.Clamp(ring, 0, sunkenTables.Length - 1)];
        }

        /// <summary>The chest table of a ring (clamped to the tables there are); null when none is set.</summary>
        public ChestSpawnTable ChestTable(int ring)
        {
            if (chestTables == null || chestTables.Length == 0) return null;
            return chestTables[Mathf.Clamp(ring, 0, chestTables.Length - 1)];
        }

        public GameObject Glow(string lootId) => lootId == "crystal" ? crystalGlow : lootId == "find-hard" ? hardGlow : null;

        public WorldLoot Loot(string id)
        {
            foreach (var e in loot) if (e.id == id && e.prefab) return e.prefab;
            return null;
        }

        public WorldCreature Creature(string role, uint pick)
        {
            foreach (var e in creatures)
                if (e.role == role && e.prefabs != null && e.prefabs.Length > 0)
                    return e.prefabs[pick % (uint)e.prefabs.Length];
            return null;
        }
    }
}
