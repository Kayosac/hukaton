using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>One hand-made chunk the valley can be assembled from: its prefab and the rules for placing it.</summary>
    [CreateAssetMenu(menuName = "Hakaton/World Assembly/Chunk", fileName = "Chunk")]
    public sealed class ChunkDefinition : ScriptableObject
    {
        [Tooltip("Stable id; the layout and saves refer to the chunk by it.")]
        public string id;
        public WorldChunk prefab;
        [Tooltip("How many of this chunk one outing may hold.")]
        [Min(0)] public int maxCount = 1;
        [Tooltip("Relative chance when filling free places.")]
        [Min(0)] public int weight = 1;
        [Tooltip("What the chunk offers: hunt, explore, fire, ice, impulse...")]
        public List<string> tags = new List<string>();
        [Tooltip("All four edges are standard, so the chunk may be turned by 90° steps.")]
        public bool rotatable = true;

        public ChunkType ToType() => new ChunkType(id, maxCount, weight, tags, rotatable);
    }
}
