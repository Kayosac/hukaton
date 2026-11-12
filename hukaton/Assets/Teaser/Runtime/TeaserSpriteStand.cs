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
            [Tooltip("World height of this sprite's canvas, metres; 0 = the stand's height.")]
            public float height;
        }

        public LootSpriteView view;
        [Tooltip("World height of the sprite canvas, metres (creatures: sprite height x creature size).")]
        [Min(.01f)] public float height = 1;
        public Key[] keys = new Key[0];

        Sprite shown;

        public override void Evaluate(float seconds)
        {
            if (!view || keys.Length == 0) return;
            Key current = keys[0];
            foreach (var key in keys)
                if (seconds >= key.seconds) current = key;
            if (current.sprite && current.sprite != shown)
            {
                view.Show(current.sprite, current.height > 0 ? current.height : height);
                shown = current.sprite;
            }
        }
    }
}
