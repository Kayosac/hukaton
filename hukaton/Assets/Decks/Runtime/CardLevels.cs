using System;
using UnityEngine;

namespace Hakaton.Decks.Unity
{
    /// <summary>A card number at levels I / II / III (board, «Карты — мердж I / II / III»).</summary>
    [Serializable]
    public struct LevelFloat
    {
        [Tooltip("Level I")] public float i;
        [Tooltip("Level II")] public float ii;
        [Tooltip("Level III")] public float iii;

        public LevelFloat(float i, float ii, float iii)
        {
            this.i = i;
            this.ii = ii;
            this.iii = iii;
        }

        public float At(int level) => level >= 3 ? iii : level == 2 ? ii : i;
        public static LevelFloat All(float value) => new LevelFloat(value, value, value);
        public override string ToString() => $"{i} / {ii} / {iii}";
    }

    /// <inheritdoc cref="LevelFloat"/>
    [Serializable]
    public struct LevelInt
    {
        [Tooltip("Level I")] public int i;
        [Tooltip("Level II")] public int ii;
        [Tooltip("Level III")] public int iii;

        public LevelInt(int i, int ii, int iii)
        {
            this.i = i;
            this.ii = ii;
            this.iii = iii;
        }

        public int At(int level) => level >= 3 ? iii : level == 2 ? ii : i;
        public int[] ToArray() => new[] { i, ii, iii };
        public static LevelInt All(int value) => new LevelInt(value, value, value);
        public static implicit operator LevelFloat(LevelInt v) => new LevelFloat(v.i, v.ii, v.iii);
        public override string ToString() => $"{i} / {ii} / {iii}";
    }
}
