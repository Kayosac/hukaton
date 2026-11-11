using System;
using System.Collections.Generic;
using Hakaton.Expeditions;
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
