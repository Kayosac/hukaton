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
            [Tooltip("The land it is built for: empty for the forest, \"swamp\" for the rotten swamp.")]
            public string biome;
            public ChunkView prefab;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>
        /// The exact variant; a swamp scene without chunks of its own yet borrows its forest stand-in
        /// (<see cref="TileShapes.StandIn"/>); otherwise any variant of the shape.
        /// </summary>
        public ChunkView Find(string shape, string variant) => Find(shape, variant, "");

        /// <summary>
        /// The chunk of this land (<paramref name="biome"/>: "" forest, <see cref="Swamp"/>); one of the forest when the
        /// land has none of its own (a landmark is one land only).
        /// </summary>
        public ChunkView Find(string shape, string variant, string biome)
        {
            biome ??= "";
            // A part of the swamp built on its own, else the swamp's mixed mire.
            if (biome.StartsWith(Swamp + "-"))
            {
                foreach (var e in entries)
                    if (e.shape == shape && e.variant == variant && e.biome == biome && e.prefab) return e.prefab;
                biome = Swamp;
            }
            var exact = FindExact(shape, variant, biome);
            if (exact) return exact;
            if (biome.Length > 0 && (exact = FindExact(shape, variant, ""))) return exact;
            string standIn = TileShapes.StandIn(variant);
            if (standIn != null && (exact = FindExact(shape, standIn, ""))) return exact;
            foreach (var e in entries)
                if (e.shape == shape && e.prefab) return e.prefab;
            return null;
        }

        public const string Swamp = "swamp";

        /// <summary>The catalogue's land of a chunk: "" the forest, "swamp" the mixed mire, "swamp-reach" and so on for the swamp's parts.</summary>
        public static string Land(Biome biome, SwampZone zone) =>
            biome != Biome.Swamp ? "" : zone == SwampZone.Mire ? Swamp : Swamp + "-" + zone.ToString().ToLowerInvariant();

        public ChunkView FindExact(string shape, string variant, string biome = "")
        {
            foreach (var e in entries)
                if (e.shape == shape && e.variant == variant && (e.biome ?? "") == (biome ?? "") && e.prefab) return e.prefab;
            // A landmark's or a swamp scene's chunks are one land's only: found whatever land is asked.
            foreach (var e in entries)
                if (e.shape == shape && e.variant == variant && e.prefab && (TileShapes.IsSwampScene(variant) || TileShapes.Get(shape).Kind == ShapeKind.Landmark)) return e.prefab;
            return null;
        }
    }
}
