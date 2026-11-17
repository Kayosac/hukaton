using System.Collections.Generic;
using Hakaton.Dungeons.Unity;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A dungeon's way in on the surface (the anthill hill): a short dark passage into the hill, the same as the one
    /// behind the dungeon's entrance chamber. Walking into it darkens the screen; at its dark end
    /// <see cref="DungeonSite"/> takes the player into the dungeon's passage.
    /// </summary>
    public sealed class DungeonEntrance : MonoBehaviour
    {
        /// <summary>Entrances in loaded chunks.</summary>
        public static readonly List<DungeonEntrance> All = new List<DungeonEntrance>();

        [Tooltip("The passage's frame: on the ground in the middle of the mouth, +Z running into the hill.")]
        public Transform passage;
        [Tooltip("Before the mouth, facing it: where the debug jump puts the player.")]
        public Transform outside;
        [Tooltip("Kept clear of the chunk's trees and props round the hill and before its mouth, m (from the passage frame).")]
        public Vector3 clearCentre = new Vector3(0, 0, 5f);
        [Min(0)] public float clearRadius = 13f;

        [HideInInspector] public DungeonSite site;
        [HideInInspector] public ChunkCoord coord;
        [HideInInspector] public int seed;
        bool laid;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Lays the passage's modules (as the dungeon lays its own) and the doorway frame at the mouth.</summary>
        public void Lay(DungeonKit kit)
        {
            if (laid || !kit || !passage) return;
            laid = true;
            DungeonBuilder.BuildPassage(kit, passage, seed);
            if (kit.doorway)
            {
                // Its room side towards the world outside, its tunnel side into the passage.
                var frame = Instantiate(kit.doorway, passage);
                frame.transform.localPosition = Vector3.zero;
                frame.transform.localRotation = Quaternion.Euler(0, 180, 0);
            }
        }

        public Vector3 ClearCentre => passage ? passage.TransformPoint(clearCentre) : transform.position;
    }
}
