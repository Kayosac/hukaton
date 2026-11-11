namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// Small events on chunks: a beast brawl, a ringing payphone, a hissing television. A share of the chunks where one
    /// fits holds one (<see cref="WorldRules.EventChance"/>; about one chunk in nine overall); none at the start. Ring 0
    /// gets only the harmless oddities; a brawl needs a roomy flat scene and rings 1–2, and its two packs replace the
    /// chunk's own beasts. Chests are separate.
    /// </summary>
    public static class WorldEvents
    {
        public const string Brawl = "brawl", Payphone = "payphone", Television = "television";

        /// <summary>Flat scenes with room for two packs to fight (an open circle of about 7 m).</summary>
        static readonly string[] Roomy = { "knoll", "grove", "camp", "farm", "yard", "ruins", "gap", "hollow", "boulders" };

        public static bool FitsBrawl(ChunkPlan plan) =>
            plan.Ring >= 1 && (plan.Shape.Kind == ShapeKind.Plain || plan.Shape.Kind == ShapeKind.PlateauTop) &&
            System.Array.IndexOf(Roomy, plan.Variant.Id) >= 0;

        /// <summary>Somewhere to stand a payphone or a television: open ground and lanes, not water, cliffs or landmarks.</summary>
        public static bool FitsOddity(ChunkPlan plan) =>
            plan.Shape.Kind == ShapeKind.Plain || plan.Shape.Kind == ShapeKind.PlateauTop || plan.Shape.Kind == ShapeKind.Maze;

        public static string Choose(ChunkPlan plan, int seed, WorldRules rules)
        {
            var c = plan.Coord;
            if (WorldRules.IsStartArea(c)) return null;
            if (Hash.Unit(seed, Salt.Event, c.X, c.Y) >= rules.EventChance[plan.Ring]) return null;
            double kind = Hash.Unit(seed, Salt.EventKind, c.X, c.Y);
            if (FitsBrawl(plan) && kind < rules.BrawlShare) return Brawl;
            if (!FitsOddity(plan)) return null;
            return Hash.Unit(seed, Salt.EventKind + 1, c.X, c.Y) < .5 ? Payphone : Television;
        }
    }
}
