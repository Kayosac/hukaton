using Hakaton.Expeditions;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// Root of one chunk prefab, built for its canonical orientation (see <see cref="TileShape"/>): pivot in the
    /// middle of the chunk at its base height. Holds the chunk's own navigation, the places where finds and
    /// creatures may appear, and the obstacles cards change.
    /// </summary>
    public sealed class ChunkView : MonoBehaviour
    {
        public string shapeId;
        public string variantId;
        [Tooltip("Baked for this chunk alone; added with the chunk's position and rotation, joined to neighbours by links.")]
        public NavMeshData navMesh;
        [Tooltip("Ground meshes that get world-space UVs once placed, so ground textures run on across chunks.")]
        public MeshFilter[] worldUv = new MeshFilter[0];
        [Tooltip("Where ordinary finds may lie.")]
        public Transform[] findSockets = new Transform[0];
        [Tooltip("The reward only cards reach: the island, the heart of a maze.")]
        public Transform pocketSocket;
        public Transform[] creatureSockets = new Transform[0];
        [Tooltip("Start overlook only: where the player stands when an outing begins.")]
        public Transform departurePose;
        public WorldObstacle[] obstacles = new WorldObstacle[0];
        [Tooltip("Ground height next to the middle of each edge segment (canonical side × 3 + segment): where links to neighbours land.")]
        public float[] edgeHeights = new float[12];
        public PhysicsProp[] props = new PhysicsProp[0];
    }
}
