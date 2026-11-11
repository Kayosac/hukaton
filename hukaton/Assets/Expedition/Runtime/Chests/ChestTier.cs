using System;
using Hakaton.Feel;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// One level of treasure chest: how much it pays, which coins fly out and how loudly it opens. The richer the
    /// tier, the more coins, the bigger the Retro Arsenal show. One asset per tier in Game/Data/Chests.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Chest tier")]
    public sealed class ChestTier : ScriptableObject
    {
        [Serializable]
        public struct CoinKind
        {
            public WorldCoin prefab;
            [Tooltip("Coins one piece of this kind is worth.")]
            [Min(1)] public int value;
            [Tooltip("How often this kind is picked while the payout is split into pieces.")]
            [Min(0)] public float weight;
        }

        [Serializable]
        public struct Burst
        {
            public VfxBurst prefab;
            [Tooltip("Seconds after the lid starts to open.")]
            [Min(0)] public float delay;
            [Tooltip("From the chest's mouth, in the chest's space (y up, -z towards the front).")]
            public Vector3 offset;
            public Color tint;
            [Min(.01f)] public float scale;
            [Tooltip("For a looping effect: seconds it keeps playing before it fades out (0: one-shot, ends by itself).")]
            [Min(0)] public float hold;
        }

        [Header("Identity")]
        public string displayName = "Сундук";
        [Range(1, 5)] public int rank = 1;
        [Tooltip("The tier's colour: coin toasts and the tint of tinted effects.")]
        public Color colour = new Color(1f, .82f, .35f);

        [Header("Payout")]
        [Tooltip("Coins in the chest: from and to, inclusive.")]
        public Vector2Int coins = new Vector2Int(5, 10);
        [Tooltip("What flies out. The payout is split into pieces of these kinds; the sum is exactly the payout.")]
        public CoinKind[] kinds = new CoinKind[0];
        [Tooltip("At most this many coins fly out; the last one carries whatever is left.")]
        [Min(1)] public int maxPieces = 12;
        [Tooltip("Optional finds besides the coins (rolled once per opening).")]
        public LootTable bonus = new LootTable { atLeastOne = false };

        [Header("Opening")]
        [Tooltip("The chest shakes before the lid gives way.")]
        [Min(0)] public float rattleSeconds = .2f;
        [Min(0)] public float rattleAngle = 3;
        [Tooltip("Coins start flying once the lid is this far open (0 shut, 1 fully open).")]
        [Range(0, 1)] public float spewAtOpenness = .45f;
        [Tooltip("Seconds over which all coins leave the chest.")]
        [Min(0)] public float spewSeconds = .5f;
        [Tooltip("Launch speed of a coin, m/s: from and to.")]
        public Vector2 launchSpeed = new Vector2(2.5f, 4f);
        [Tooltip("Cone around 'up and a bit forward' the coins fly in, degrees.")]
        [Range(0, 80)] public float spreadAngle = 30;

        [Header("Look and sound (Retro Arsenal)")]
        [Tooltip("Looping marker while the chest is shut, so it reads from afar. Released when it opens.")]
        public VfxBurst idleMarker;
        public Vector3 idleOffset = new Vector3(0, .1f, 0);
        public Color idleTint = Color.white;
        [Min(.01f)] public float idleScale = 1;
        [Tooltip("Looping glow out of the open chest while the coins fly, then released.")]
        public VfxBurst openGlow;
        public Color openGlowTint = Color.white;
        [Min(.01f)] public float openGlowScale = 1;
        [Min(0)] public float openGlowSeconds = 1.5f;
        [Tooltip("One-shot effects of the opening, each with its own delay.")]
        public Burst[] bursts = new Burst[0];
        public SfxCue rattleCue, openCue, spewCue;
        [Tooltip("Spew cue repeats every this many coins.")]
        [Min(1)] public int spewCueEvery = 3;

        [Header("Feel")]
        [Range(0, 1)] public float trauma = .05f;
        public float fovPunch;
        [Tooltip("A short freeze as the lid flies open (0: none).")]
        [Min(0)] public float hitStop;
        [Tooltip("Shown on the screen when it opens (empty: nothing).")]
        public string announcement;

        /// <summary>A payout between the tier's bounds.</summary>
        public int RollCoins(Func<float> next)
        {
            int low = Mathf.Max(0, Mathf.Min(coins.x, coins.y)), high = Mathf.Max(coins.x, coins.y);
            return low + Mathf.Min(high - low, (int)(next() * (high - low + 1)));
        }

        /// <summary>
        /// Splits <paramref name="total"/> into pieces of the tier's kinds; the values add up to exactly the total.
        /// Kinds are picked by weight among those that still fit; the last allowed piece carries the remainder.
        /// </summary>
        public void Split(int total, Func<float> next, System.Collections.Generic.List<(WorldCoin prefab, int value)> into)
        {
            if (into == null || total <= 0 || kinds == null || kinds.Length == 0) return;
            int left = total;
            while (left > 0)
            {
                int index = Pick(left, next);
                if (index < 0) index = Smallest();
                if (index < 0) return;
                int value = into.Count >= maxPieces - 1 ? left : Mathf.Min(left, kinds[index].value);
                into.Add((kinds[index].prefab, value));
                left -= value;
            }
        }

        int Pick(int left, Func<float> next)
        {
            float total = 0;
            for (int i = 0; i < kinds.Length; i++) if (Fits(i, left)) total += kinds[i].weight;
            if (total <= 0) return -1;
            float pick = next() * total;
            int last = -1;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (!Fits(i, left)) continue;
                last = i;
                pick -= kinds[i].weight;
                if (pick < 0) return i;
            }
            return last;
        }

        bool Fits(int i, int left) => kinds[i].prefab && kinds[i].weight > 0 && kinds[i].value <= left;

        int Smallest()
        {
            int best = -1;
            for (int i = 0; i < kinds.Length; i++)
                if (kinds[i].prefab && (best < 0 || kinds[i].value < kinds[best].value)) best = i;
            return best;
        }
    }
}
