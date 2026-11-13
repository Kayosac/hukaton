using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.OldApiary;
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
        [Tooltip("Loose things the player and cards push about (crates, sacks, sticks, light logs), as in the Old Apiary.")]
        public PhysicsProp[] props = new PhysicsProp[0];
        [Tooltip("What Fire and Lightning set alight: grass, bushes, young trees, wood, sacks (the Old Apiary's rules).")]
        public Burnable[] burnables = new Burnable[0];
        [Tooltip("Trees «Оживление дерева» can bring to life.")]
        public AwakenableTree[] trees = new AwakenableTree[0];
        [Tooltip("Places that sound (a campfire, a chimney): the streamer keeps the nearest few looping.")]
        public AmbientSpot[] ambient = new AmbientSpot[0];
        [Tooltip("High places that show far landmarks for a while.")]
        public Lookout[] lookouts = new Lookout[0];
        [Tooltip("Rotten swamp: gas vents (the streamer hands them the outing so Fire and Lightning find them).")]
        public GasVent[] gasVents = new GasVent[0];
        [Tooltip("Rotten swamp: where natural tar pools lie (the streamer lays a tar puddle on each, World content → Tar puddle).")]
        public SwampTarSpot[] tarSpots = new SwampTarSpot[0];
        [Tooltip("Where a road runs inside this chunk when it has to (up a ramp): local points from one end to the other.")]
        public Vector3[] roadGuide = new Vector3[0];
    }
}
