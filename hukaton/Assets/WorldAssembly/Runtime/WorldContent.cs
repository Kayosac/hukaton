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
