using System;
using System.Collections.Generic;

namespace Hakaton.SpecialItems
{
    /// <summary>The special items part of the game save. Null in saves made before special items.</summary>
    [Serializable]
    public sealed class SpecialItemsSave
    {
        /// <summary>Ids of the items bought (kept forever).</summary>
        public List<string> owned = new List<string>();
        /// <summary>Items the dealer has already introduced.</summary>
        public List<string> introduced = new List<string>();
        /// <summary>Items the player has already looked at on the table (no longer blinking as new).</summary>
        public List<string> seen = new List<string>();
        /// <summary>The item a short Q takes out (the last one chosen).</summary>
        public string lastSelected = "";
        /// <summary>Steps of the phone's battery bought on the PC.</summary>
        public int phoneBattery;
    }

    /// <summary>
    /// What the player has of special items: bought ones, the dealer's introductions, the last one chosen with Q and the
    /// phone battery's steps. A pure model; the game save keeps it (<see cref="Save"/> / <see cref="Restore"/>).
    /// </summary>
    public sealed class SpecialItemInventory
    {
        readonly List<string> owned = new List<string>(), introduced = new List<string>(), seen = new List<string>();

        public IReadOnlyList<string> Owned => owned;
        public string LastSelected { get; private set; } = "";
        public int PhoneBatteryLevel { get; private set; }
        /// <summary>A purchase, a choice with Q, an introduction heard, a battery step (anything the save must keep).</summary>
        public event Action Changed;

        public bool Owns(string id) => !string.IsNullOrEmpty(id) && owned.Contains(id);
        public bool WasIntroduced(string id) => introduced.Contains(id);
        public bool WasSeen(string id) => seen.Contains(id);

        public bool Add(string id)
        {
            if (string.IsNullOrEmpty(id) || owned.Contains(id)) return false;
            owned.Add(id);
            if (string.IsNullOrEmpty(LastSelected)) LastSelected = id;
            Changed?.Invoke();
            return true;
        }

        public void Select(string id)
        {
            if (!Owns(id) || LastSelected == id) return;
            LastSelected = id;
            Changed?.Invoke();
        }

        public void MarkIntroduced(string id)
        {
            if (string.IsNullOrEmpty(id) || introduced.Contains(id)) return;
            introduced.Add(id);
            Changed?.Invoke();
        }

        public void MarkSeen(string id)
        {
            if (string.IsNullOrEmpty(id) || seen.Contains(id)) return;
            seen.Add(id);
            Changed?.Invoke();
        }

        public void SetPhoneBattery(int level)
        {
            level = Math.Max(0, level);
            if (level == PhoneBatteryLevel) return;
            PhoneBatteryLevel = level;
            Changed?.Invoke();
        }

        public SpecialItemsSave Save() => new SpecialItemsSave
        {
            owned = new List<string>(owned), introduced = new List<string>(introduced), seen = new List<string>(seen),
            lastSelected = LastSelected ?? "", phoneBattery = PhoneBatteryLevel
        };

        /// <summary>A saved state; ids the game no longer knows are dropped. Null (an older save): nothing bought yet.</summary>
        public void Restore(SpecialItemsSave save, Func<string, bool> known)
        {
            owned.Clear(); introduced.Clear(); seen.Clear();
            LastSelected = "";
            PhoneBatteryLevel = 0;
            if (save != null)
            {
                bool Keep(string id) => !string.IsNullOrEmpty(id) && (known == null || known(id));
                foreach (var id in save.owned ?? new List<string>()) if (Keep(id) && !owned.Contains(id)) owned.Add(id);
                foreach (var id in save.introduced ?? new List<string>()) if (Keep(id) && !introduced.Contains(id)) introduced.Add(id);
                foreach (var id in save.seen ?? new List<string>()) if (Keep(id) && !seen.Contains(id)) seen.Add(id);
                LastSelected = owned.Contains(save.lastSelected) ? save.lastSelected : owned.Count > 0 ? owned[0] : "";
                PhoneBatteryLevel = Math.Max(0, save.phoneBattery);
            }
            Changed?.Invoke();
        }
    }
}
