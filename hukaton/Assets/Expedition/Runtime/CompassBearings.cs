using System;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>What the ant compass (special item «Компас-муравей») is drawn to.</summary>
    public enum CompassTargetKind
    {
        /// <summary>Nothing it can smell: the ant wanders round the dial.</summary>
        None,
        /// <summary>Above ground: the nearest dungeon entrance (an anthill).</summary>
        Entrance,
        /// <summary>In a dungeon: the nearest stash whose chest is still shut.</summary>
        Stash,
        /// <summary>In a dungeon with every stash opened: the way back up.</summary>
        WayOut,
    }

    /// <summary>Where the ant compass points: a kind and a point in the world.</summary>
    public readonly struct CompassTarget
    {
        public readonly CompassTargetKind Kind;
        public readonly Vector3 Point;
        public CompassTarget(CompassTargetKind kind, Vector3 point) { Kind = kind; Point = point; }
        public bool Found => Kind != CompassTargetKind.None;
    }

    /// <summary>
    /// The ant compass asks here what it is drawn to; the place that knows the dungeons (the endless world's dungeon
    /// site) answers. No answer (no dungeons in this world): <see cref="CompassTargetKind.None"/>.
    /// </summary>
    public static class CompassBearings
    {
        public static Func<Vector3, CompassTarget> Source { get; set; }

        public static CompassTarget Find(Vector3 from) => Source != null ? Source(from) : default;
    }
}
