using System;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>
    /// Shows existing drawings through the game's loot sprite view (same hand-drawn material, pixel cell and wobble as
    /// creatures and shelf items), at a given world height, switching drawing on cue.
    /// </summary>
    public sealed class TeaserSpriteStand : TeaserTrack
    {
        [Serializable]
        public struct Key
        {
            public float seconds;
            public Sprite sprite;
        }

        public LootSpriteView view;
        [Tooltip("World height of the sprite canvas, metres (creatures: sprite height x creature size).")]
        [Min(.01f)] public float height = 1;
        public Key[] keys = new Key[0];

        Sprite shown;

        public override void Evaluate(float seconds)
        {
            if (!view || keys.Length == 0) return;
            Sprite sprite = keys[0].sprite;
            foreach (var key in keys)
                if (seconds >= key.seconds) sprite = key.sprite;
            if (sprite && sprite != shown)
            {
                view.Show(sprite, height);
                shown = sprite;
            }
        }
    }
}
