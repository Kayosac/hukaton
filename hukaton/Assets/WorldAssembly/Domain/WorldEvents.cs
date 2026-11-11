namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// Small events on chunks: a beast brawl, a ringing payphone, a hissing television — none in ring 0. A brawl takes
    /// a roomy flat scene (<see cref="WorldRules.BrawlChance"/>) and its two packs replace the chunk's own beasts; a
    /// payphone or a television is very rare (<see cref="WorldRules.OddityChance"/>, about one chunk in a hundred and
    /// fifty). Chests are separate.
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
            plan.Ring >= 1 && (plan.Shape.Kind == ShapeKind.Plain || plan.Shape.Kind == ShapeKind.PlateauTop || plan.Shape.Kind == ShapeKind.Maze);

        public static string Choose(ChunkPlan plan, int seed, WorldRules rules)
        {
            var c = plan.Coord;
            if (WorldRules.IsStartArea(c) || plan.Ring == 0) return null;
            if (FitsBrawl(plan) && Hash.Unit(seed, Salt.Event, c.X, c.Y) < rules.BrawlChance[plan.Ring]) return Brawl;
            if (FitsOddity(plan) && Hash.Unit(seed, Salt.EventKind, c.X, c.Y) < rules.OddityChance[plan.Ring])
                return Hash.Unit(seed, Salt.EventKind + 1, c.X, c.Y) < .5 ? Payphone : Television;
            return null;
        }
    }
}
