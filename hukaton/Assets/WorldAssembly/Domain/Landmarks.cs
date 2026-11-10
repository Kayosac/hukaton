using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// A set piece bigger than one chunk (Kayospace «Открытый мир v2»: ориентиры на несколько чанков): Size × Size flat
    /// low chunks built as one piece of ground and cut into part prefabs. Every part is one walkable region open on
    /// all edges, so a landmark joins anything flat around it; its relief lives inside and meets the neighbours at
    /// ground level. The start hill is one too, always at the centre of the world.
    /// </summary>
    public sealed class LandmarkKind
    {
        public readonly string Id;
        public readonly int Size;
        /// <summary>The part with the landmark's reward and its guard (canonical part indices), or none.</summary>
        public readonly int FocusI, FocusJ;
        public bool HasFocus => FocusI >= 0;

        public LandmarkKind(string id, int size, int focusI = -1, int focusJ = -1) { Id = id; Size = size; FocusI = focusI; FocusJ = focusJ; }

        public string PartId(int i, int j) => $"{Id}-{i}{j}";

        /// <summary>Canonical part at world offset (wi, wj) inside the landmark, turned by quarter turns clockwise.</summary>
        public (int i, int j) PartAt(int rotation, int wi, int wj)
        {
            int n = Size - 1;
            switch (rotation & 3)
            {
                case 1: return (n - wj, wi);
                case 2: return (n - wi, n - wj);
                case 3: return (wj, n - wi);
                default: return (wi, wj);
            }
        }
    }

    public static class Landmarks
    {
        /// <summary>The hill the player starts on: 3 × 3 chunks round (0, 0), the top in the middle chunk.</summary>
        public static readonly LandmarkKind StartHill = new LandmarkKind("start-hill", 3);

        /// <summary>Landmarks the district planner places.</summary>
        public static readonly LandmarkKind[] Placed =
        {
            new LandmarkKind("watchhill", 2, 1, 1),
            new LandmarkKind("crater", 2, 1, 1),
            new LandmarkKind("fort", 2, 1, 0),
        };

        public static IEnumerable<LandmarkKind> All
        {
            get
            {
                yield return StartHill;
                foreach (var k in Placed) yield return k;
            }
        }

        public static LandmarkKind Get(string id)
        {
            foreach (var k in All) if (k.Id == id) return k;
            throw new ArgumentException("no landmark " + id);
        }

        /// <summary>Landmark and part of a landmark part shape, from its id.</summary>
        public static (LandmarkKind kind, int i, int j) Parse(string shapeId)
        {
            int dash = shapeId.LastIndexOf('-');
            var kind = Get(shapeId.Substring(0, dash));
            return (kind, shapeId[dash + 1] - '0', shapeId[dash + 2] - '0');
        }
    }
}
