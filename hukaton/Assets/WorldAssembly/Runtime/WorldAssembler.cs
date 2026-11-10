using System.Collections.Generic;
using System.Diagnostics;
using Unity.AI.Navigation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// Builds one outing's valley out of authored chunk prefabs: lays them out from a seed
    /// (<see cref="LayoutGenerator"/>), instantiates the start overlook, the chunks and the border pieces under
    /// <see cref="root"/>, then bakes the navigation for the creatures. Nothing is generated here except the
    /// arrangement: every chunk, border and creature is an already configured prefab.
    /// </summary>
    public sealed class WorldAssembler : MonoBehaviour
    {
        [Header("Chunks")]
        [Tooltip("Side of a chunk in metres; every chunk prefab is built for this size.")]
        [Min(1)] public float chunkSize = 40f;
        [Tooltip("Middle part of every edge kept clear at ground height 0 for the path between chunks.")]
        [Min(0)] public float gateWidth = 8f;
        [Tooltip("The constant start overlook at (0, 0), looking north (+Z).")]
        public ChunkDefinition startChunk;
        public List<ChunkDefinition> catalogue = new List<ChunkDefinition>();
        [Tooltip("Closes an edge that faces nothing. Built for the north edge: pivot at the edge's middle, +Z pointing out of the valley.")]
        public GameObject borderPrefab;

        [Header("Layout rules")]
        [Min(0)] public int chunkCount = 7;
        public List<string> requiredTags = new List<string> { "hunt", "explore" };
        [Min(0)] public int halfWidth = 2;
        [Min(1)] public int depth = 3;

        [Header("Scene")]
        [Tooltip("Assembled chunks live here; it is emptied before every assembly.")]
        public Transform root;
        [Tooltip("Collects the assembled chunks (Collect Objects: Children of root).")]
        public NavMeshSurface navigation;

        public WorldLayout Layout { get; private set; }
        public Transform DeparturePose { get; private set; }
        public IReadOnlyList<WorldChunk> Chunks => chunks;
        /// <summary>Time of the last assembly: instantiating and baking, in milliseconds.</summary>
        public double LastInstantiateMs { get; private set; }
        public double LastBakeMs { get; private set; }

        readonly List<WorldChunk> chunks = new List<WorldChunk>();

        public WorldLayout Assemble(int seed)
        {
            Clear();
            var types = new List<ChunkType>();
            var byId = new Dictionary<string, ChunkDefinition>();
            foreach (var definition in catalogue)
            {
                if (!definition || !definition.prefab || string.IsNullOrEmpty(definition.id)) continue;
                types.Add(definition.ToType());
                byId[definition.id] = definition;
            }
            var rules = new LayoutRules { ChunkCount = chunkCount, RequiredTags = requiredTags, HalfWidth = halfWidth, Depth = depth };
            Layout = LayoutGenerator.Generate(seed, types, rules);

            var clock = Stopwatch.StartNew();
            var start = Place(startChunk ? startChunk.prefab : null, Layout.Start, 0, "Start");
            DeparturePose = start && start.departurePose ? start.departurePose : root;
            foreach (var placement in Layout.Chunks)
                Place(byId[placement.TypeId].prefab, placement.Cell, placement.Rotation, placement.TypeId);
            if (borderPrefab)
                foreach (var (cell, side) in Layout.OpenEdges())
                {
                    var border = Instantiate(borderPrefab, root);
                    border.name = $"Border {cell} {side}";
                    border.transform.localPosition = CellCentre(cell) + EdgeOffset(side);
                    border.transform.localRotation = Quaternion.Euler(0, 90 * (int)side, 0);
                }
            LastInstantiateMs = clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            if (navigation)
            {
                Physics.SyncTransforms();
                navigation.BuildNavMesh();
            }
            LastBakeMs = clock.Elapsed.TotalMilliseconds;
            Debug.Log($"[World] seed {seed}: {Layout.Chunks.Count} chunks + start, instantiate {LastInstantiateMs:0} ms, navmesh {LastBakeMs:0} ms");
            return Layout;
        }

        public void Clear()
        {
            chunks.Clear();
            Layout = null;
            DeparturePose = null;
            if (navigation) navigation.RemoveData();
            if (!root) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                if (Application.isPlaying) { child.SetActive(false); Destroy(child); }
                else DestroyImmediate(child);
            }
        }

        WorldChunk Place(WorldChunk prefab, Cell cell, int rotation, string label)
        {
            if (!prefab) return null;
            var chunk = Instantiate(prefab, root);
            chunk.name = $"{label} {cell}";
            chunk.transform.localPosition = CellCentre(cell);
            chunk.transform.localRotation = Quaternion.Euler(0, 90 * rotation, 0);
            chunks.Add(chunk);
            return chunk;
        }

        Vector3 CellCentre(Cell cell) => new Vector3(cell.X * chunkSize, 0, cell.Y * chunkSize);

        Vector3 EdgeOffset(Side side) => side switch
        {
            Side.North => new Vector3(0, 0, chunkSize / 2),
            Side.East => new Vector3(chunkSize / 2, 0, 0),
            Side.South => new Vector3(0, 0, -chunkSize / 2),
            _ => new Vector3(-chunkSize / 2, 0, 0),
        };

        [ContextMenu("Preview: assemble seed 1")]
        void PreviewOne() => Assemble(1);

        [ContextMenu("Preview: next seed")]
        void PreviewNext() => Assemble((Layout?.Seed ?? 0) + 1);

        [ContextMenu("Preview: clear")]
        void PreviewClear() => Clear();
    }
}
