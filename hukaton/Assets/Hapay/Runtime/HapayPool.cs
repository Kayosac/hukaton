using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Hapay
{
    /// <summary>One thing a collector wants: an item (by loot id) and how many.</summary>
    [Serializable]
    public sealed class HapayWant
    {
        [Tooltip("Loot item id (Assets/Loot/Items).")]
        public string item;
        [Min(1)] public int count = 1;

        public HapayWant() { }
        public HapayWant(string item, int count) { this.item = item; this.count = count; }
    }

    /// <summary>A collector's request on хаPAY: who asks (nick, avatar, a line) and the set he pays for.</summary>
    [Serializable]
    public sealed class HapayCollection
    {
        [Tooltip("Stable id used by saves. Never change it once shipped.")]
        public string id;
        public string nick;
        [Tooltip("Index into the pool's avatars.")]
        [Min(0)] public int avatar;
        [TextArea(1, 3)] public string quote;
        public List<HapayWant> wants = new List<HapayWant>();

        /// <summary>All pieces of the set (a want of 12 counts as 12).</summary>
        public int Pieces
        {
            get
            {
                int n = 0;
                foreach (var want in wants) if (want != null) n += Math.Max(1, want.count);
                return n;
            }
        }
    }

    /// <summary>
    /// хаPAY (board «Спецпредметы»): the collectors' requests and the site's numbers. The site shows <see cref="lotsOnSite"/>
    /// of them at a time; each lives for a time that grows with its size, and pays the sum of the items' recommended prices
    /// times a multiplier that grows with the size and the rarity.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/хаPAY/Пул коллекционеров", fileName = "Hapay pool")]
    public sealed class HapayPool : ScriptableObject
    {
        public List<HapayCollection> collections = new List<HapayCollection>();
        [Tooltip("Collectors' avatars (silhouettes on grey), indexed by HapayCollection.avatar.")]
        public Sprite[] avatars = Array.Empty<Sprite>();
        [Tooltip("The first lot after the account is bought: who asks (its wants are made from what the player has).")]
        public HapayCollection firstLot = new HapayCollection { id = "first", nick = "старый_клиент" };
        [Min(1)] public int firstLotMaxKinds = 3;

        [Header("Site")]
        [Min(1)] public int lotsOnSite = 4;
        [Tooltip("A lot that has just left does not come back for this many lot changes.")]
        [Min(0)] public int noRepeat = 5;
        [Tooltip("A new lot leans to sets the player already has: a fully stocked set comes this many times more often than one with nothing (0: plain random).")]
        [Min(0)] public float ownedBias = 6;

        [Header("Time (minutes of play)")]
        [Min(.1f)] public float shortestMinutes = 8;
        [Min(.1f)] public float longestMinutes = 25;
        [Tooltip("Pieces in a set: 2 lives the shortest, this many or more the longest.")]
        [Min(3)] public int bigSetPieces = 9;

        [Header("Price: sum of recommended prices × multiplier")]
        public float baseMultiplier = 1.3f;
        [Tooltip("Added for every piece above two.")]
        public float perPiece = .04f;
        [Tooltip("Added per step of the set's average rarity (0 common … 3 legendary).")]
        public float perRarity = .05f;
        public float maxMultiplier = 1.6f;
        [Tooltip("The price is rounded to this many coins.")]
        [Min(1)] public int roundTo = 5;

        public HapayCollection Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (firstLot != null && firstLot.id == id) return firstLot;
            foreach (var collection in collections) if (collection != null && collection.id == id) return collection;
            return null;
        }

        public float SecondsFor(int pieces)
        {
            float k = Mathf.Clamp01((pieces - 2f) / Mathf.Max(1, bigSetPieces - 2));
            return Mathf.Lerp(shortestMinutes, longestMinutes, k) * 60;
        }

        public Sprite Avatar(int index) => avatars != null && avatars.Length > 0 ? avatars[Mathf.Clamp(index, 0, avatars.Length - 1)] : null;
    }
}
