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

        [Tooltip("Lay the dungeon's own passage modules in the passage (off when the hill model has its own tunnel).")]
        public bool layModules = true;
        [Tooltip("The darkness panes across the passage: half width and height of the tunnel, from and to how deep, m.")]
        public float veilHalfWidth = 2.3f, veilHeight = 6.6f, veilFrom = 1f, veilTo = 7.35f;

        [HideInInspector] public DungeonSite site;
        [HideInInspector] public ChunkCoord coord;
        [HideInInspector] public int seed;
        bool laid;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Lays the passage: its modules and doorway frame (as the dungeon lays its own) unless the hill has its own tunnel, and its darkness.</summary>
        public void Lay(DungeonKit kit)
        {
            if (laid || !kit || !passage) return;
            laid = true;
            if (layModules) DungeonBuilder.BuildPassage(kit, passage, seed);
            // Darkness thickening towards the dead end: from outside the passage reads as a way down into the dark.
            DungeonBuilder.Veil(passage, kit.passageDark, veilHalfWidth, veilHeight, veilFrom, veilTo, 12, Color.black, .05f, .3f, 1f, "Darkness");
            if (layModules && kit.doorway)
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
