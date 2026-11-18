using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Dungeons.Unity
{
    /// <summary>
    /// One dungeon kind (e.g. the anthill): its earth-shell modules, its hand-made chambers, corridor dressing and the
    /// planner's numbers. Module conventions (as the AnthillArt models are built): pivot at floor level in the middle of
    /// the cell or edge, room side facing +Z; the corner covers its cell's south and west edges; the ramp rises 1 m
    /// towards −Z over one cell; the vault sits on the wall tops and spans walls at x = ±2.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Dungeon Kit", fileName = "Dungeon Kit")]
    public sealed class DungeonKit : ScriptableObject
    {
        [Serializable]
        public struct Dressing
        {
            public GameObject prefab;
            [Tooltip("Chance per eligible spot.")]
            [Range(0, 1)] public float chance;
            [Tooltip("Random extra yaw, degrees.")]
            public float yawJitter;
            public Vector2 scaleRange;
        }

        /// <summary>Who lives at a chamber's markers when the dungeon is first laid (spawned by the game's dungeon site).</summary>
        [Serializable]
        public struct Garrison
        {
            public RoomRole role;
            [Tooltip("Marker name in the chamber: Spawn, Spawn MachineGun, Spawn Larva, Spawn Boss.")]
            public string marker;
            [Tooltip("Which of the chamber's markers of that name (0 = the first); -1 = every one of them.")]
            public int slot;
            [Tooltip("The creature prefab (a WorldCreature). Squad creatures form a patrol round the marker.")]
            public GameObject creature;
            [Tooltip("How many at the marker: from and to.")]
            public Vector2Int count;
            [Range(0, 1)] public float chance;
        }

        [Header("Earth shell")]
        public GameObject[] walls;
        public GameObject corner;
        public GameObject doorway;
        public GameObject vault;
        public GameObject[] floors;
        public GameObject ramp;
        public GameObject ventCollar;
        [Tooltip("Variants of the corner and the vault (the single fields above are used when these are empty).")]
        public GameObject[] corners = new GameObject[0];
        public GameObject[] vaults = new GameObject[0];
        [Tooltip("Dark earth behind every cell's modules: what shows through any seam between them.")]
        public Material backstop;

        [Header("Chambers")]
        public List<ChamberTemplate> chambers = new List<ChamberTemplate>();

        [Header("Dressing")]
        [Tooltip("Stood against a wall, facing into the corridor or room (niches, wall roots, resin, wall fungi, lantern brackets).")]
        public List<Dressing> wallDressing = new List<Dressing>();
        [Tooltip("Stood on the floor of corridor cells (glow clusters, clods, eggs...).")]
        public List<Dressing> corridorFloorDressing = new List<Dressing>();
        [Tooltip("Hung from the vault (chains, root tangles).")]
        public List<Dressing> ceilingDressing = new List<Dressing>();
        [Tooltip("A light every this many corridor cells (a wall dressing with a light is preferred there).")]
        [Min(1)] public int corridorLightEvery = 3;
        public GameObject corridorLight;
        [Tooltip("The corridor lantern's own light is a candle; this light is added to it so the way stays readable.")]
        public Color corridorLightColor = new Color(1f, .62f, .32f);
        [Min(0)] public float corridorLightRange = 7f;
        [Min(0)] public float corridorLightIntensity = 1.8f;

        [Header("Inhabitants")]
        public List<Garrison> garrison = new List<Garrison>();

        [Header("Rewards")]
        [Tooltip("The dungeon's prize chest (a TreasureChest prefab: rich, with a guaranteed rare curio): at the goal chamber's " +
                 "Reward marker, or, where the goal has none (the queen's chamber), under the way up.")]
        public GameObject prizeChest;
        [Tooltip("At every other Reward marker, by chance.")]
        public GameObject sideChest;
        [Range(0, 1)] public float sideChestChance = .6f;

        [Header("Floor modifiers")]
        [Tooltip("Chance that a level has a modifier (acid or collapses); each level rolls on its own.")]
        [Range(0, 1)] public float modifierChance = .5f;
        [Tooltip("Acid: puddles that poison the player standing in them (Яд).")]
        public GameObject[] acidPuddles = new GameObject[0];
        public Material acidMaterial;
        public Color acidGlow = new Color(.55f, 1f, .25f);
        [Range(0, 1)] public float acidPerCorridorCell = .3f;
        [Range(0, 1)] public float acidPerRoomCell = .08f;
        [Tooltip("Collapses: heaps of fallen earth against the walls, and the ceiling coming down now and then near the player.")]
        public GameObject[] rubble = new GameObject[0];
        [Range(0, 1)] public float rubblePerCorridorCell = .25f;
        [Range(0, 1)] public float rubblePerRoomWall = .06f;
        [Tooltip("Loose chunks that fall from the ceiling (each child of this prefab is one chunk).")]
        public GameObject debris;
        [Tooltip("Seconds between ceiling falls on a collapse level: from and to.")]
        public Vector2 collapseEvery = new Vector2(7, 14);
        [Min(0)] public int collapseHalfHearts = 2;
        [Tooltip("Dust before and on a fall (a VfxBurst prefab).")]
        public GameObject collapseDust;

        [Header("Hidden stashes")]
        [Tooltip("What closes a stash, one picked per stash: prefabs that a card opens (a root curtain Fire burns, a rubble plug Сквозняк blows away). Laid across the stash's first cell, +Z into the stash.")]
        public GameObject[] stashLocks = new GameObject[0];
        [Tooltip("The chest at a stash's dead end.")]
        public GameObject stashChest;

        [Header("The way out")]
        [Tooltip("Transparent black (alpha blended, back faces culled): veils in the surface passage that make its dead end read as depth.")]
        public Material passageDark;
        [Tooltip("Additive: the glow in the dungeon's passage that reads as daylight at its end.")]
        public Material passageGlow;
        public Color surfaceLight = new Color(1f, .95f, .82f);
        [Min(0)] public float surfaceLightIntensity = 2.5f;
        [Min(0)] public float surfaceLightRange = 10f;

        [Header("Atmosphere underground")]
        public Color ambient = new Color(.16f, .12f, .1f);
        public Color fogColor = new Color(.05f, .035f, .03f);
        [Min(0)] public float fogDensity = .025f;
        [Tooltip("How much of the open world's colour grade stays on underground (0 none, 1 all).")]
        [Range(0, 1)] public float postProcessWeight = .15f;

        [Header("Planner")]
        [Range(0, 1)] public float largeChance = .4f;
        [Range(0, 1)] public float threeLevelChance = .5f;
        [Range(0, 1)] public float smallBranchChance = .5f;
        [Min(1)] public int corridorMin = 2;
        [Min(1)] public int corridorMax = 6;
        [Min(1)] public int levelDrop = 6;
        [Range(0, 1)] public float turnChance = .3f;
        [Tooltip("Hidden stashes per dungeon: from and to.")]
        public Vector2Int stashes = new Vector2Int(1, 2);

        public DungeonRecipe Recipe()
        {
            var recipe = new DungeonRecipe
            {
                LargeChance = largeChance,
                ThreeLevelChance = threeLevelChance,
                SmallBranchChance = smallBranchChance,
                CorridorMin = corridorMin,
                CorridorMax = Mathf.Max(corridorMin, corridorMax),
                LevelDrop = levelDrop,
                TurnChance = turnChance,
                StashMin = Mathf.Max(0, Mathf.Min(stashes.x, stashes.y)),
                StashMax = Mathf.Max(stashes.x, stashes.y),
            };
            foreach (var c in chambers) if (c) recipe.Shapes.Add(c.ToShape());
            return recipe;
        }

        public ChamberTemplate Chamber(string id) => chambers.Find(c => c && c.id == id);
    }
}
