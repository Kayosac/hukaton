using System;
using System.Collections.Generic;
using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>Chunk prefabs by shape and variant id (the ids of <see cref="TileShapes"/>).</summary>
    [CreateAssetMenu(menuName = "Hakaton/World Assembly/Chunk catalogue", fileName = "Chunk catalogue")]
    public sealed class ChunkCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string shape;
            public string variant;
            public ChunkView prefab;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>The exact variant, or any variant of the shape when that one is missing.</summary>
        public ChunkView Find(string shape, string variant)
        {
            ChunkView fallback = null;
            foreach (var e in entries)
            {
                if (e.shape != shape || !e.prefab) continue;
                if (e.variant == variant) return e.prefab;
                if (!fallback) fallback = e.prefab;
            }
            return fallback;
        }
    }
}
