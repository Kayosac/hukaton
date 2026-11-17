using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A dungeon's way in on the surface (the anthill mound): walking into the dark end of its throat takes the player
    /// down (<see cref="DungeonSite.Enter"/>). Coming back up, the player stands at <see cref="outside"/>.
    /// </summary>
    public sealed class DungeonEntrance : MonoBehaviour
    {
        [Tooltip("The dark end of the throat: stepping here goes down.")]
        public Transform inside;
        [Tooltip("In front of the mouth, facing away from the mound: where the player comes out.")]
        public Transform outside;
        [Min(.3f)] public float reach = 1.4f;

        [HideInInspector] public DungeonSite site;
        [HideInInspector] public ChunkCoord coord;
        [HideInInspector] public int seed;

        void Update()
        {
            if (!site || !inside || !site.CanEnter) return;
            var p = site.PlayerPosition;
            var d = p - inside.position;
            if (Mathf.Abs(d.y) < 2.2f && new Vector2(d.x, d.z).sqrMagnitude < reach * reach) site.Enter(this);
        }
    }
}
