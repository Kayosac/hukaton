using Hakaton.Feel;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// How one kind of loose thing breaks (a clay pot, a crate, a bottle, a lantern): how hard a blow it takes, what
    /// it falls apart into, how it sounds and what may be inside. One asset per kind in Game/Data/Breakables.
    /// Speeds are the change of the body's speed in m/s (a card's knock, a blast, a crash against something).
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Breakable kind")]
    public sealed class BreakableKind : ScriptableObject
    {
        [Header("How hard")]
        [Tooltip("A blow (a card's knock, a blast) that changes its speed by this much breaks it.")]
        [Min(.1f)] public float hitSpeed = 2.5f;
        [Tooltip("Crashing into something (falling, thrown against a wall) this fast breaks it.")]
        [Min(.1f)] public float crashSpeed = 4.5f;

        [Header("Pieces")]
        public IceShard[] shards = new IceShard[0];
        public Vector2Int shardCount = new Vector2Int(4, 6);
        [Tooltip("Scale of a piece against its prefab, from and to.")]
        public Vector2 shardScale = new Vector2(.7f, 1.2f);
        [Tooltip("Speed the pieces fly out with, m/s.")]
        public Vector2 shardSpeed = new Vector2(1.5f, 3.5f);
        public Vector2 shardLifetime = new Vector2(3, 5);

        [Header("Show")]
        public VfxBurst burst;
        public Color burstTint = Color.white;
        [Min(.01f)] public float burstScale = .7f;
        public SfxCue sound;

        [Header("Inside (each rolled once, when it breaks)")]
        [Range(0, 1)] public float coinChance;
        public WorldCoin coin;
        [Tooltip("Coins (each worth one) when they are there: from and to, inclusive.")]
        public Vector2Int coins = new Vector2Int(1, 3);
        [Range(0, 1)] public float itemChance;
        public LootPool items;
    }
}
