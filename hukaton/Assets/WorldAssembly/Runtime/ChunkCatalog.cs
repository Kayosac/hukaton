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

        /// <summary>
        /// The exact variant; a swamp scene without chunks of its own yet borrows its forest stand-in
        /// (<see cref="TileShapes.StandIn"/>); otherwise any variant of the shape.
        /// </summary>
        public ChunkView Find(string shape, string variant)
        {
            var exact = FindExact(shape, variant);
            if (exact) return exact;
            string standIn = TileShapes.StandIn(variant);
            if (standIn != null && (exact = FindExact(shape, standIn))) return exact;
            foreach (var e in entries)
                if (e.shape == shape && e.prefab) return e.prefab;
            return null;
        }

        public ChunkView FindExact(string shape, string variant)
        {
            foreach (var e in entries)
                if (e.shape == shape && e.variant == variant && e.prefab) return e.prefab;
            return null;
        }
    }
}
