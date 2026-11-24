namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// Small events on chunks: a beast brawl, a ringing payphone, a hissing television; none at the start. A brawl takes
    /// a roomy flat scene in any ring (<see cref="WorldRules.BrawlChance"/>) and its two packs replace the chunk's own
    /// beasts; a payphone or a television is very rare and only in rings 1–2 (<see cref="WorldRules.OddityChance"/>,
    /// about one chunk in a hundred and sixty). Chests are separate.
    /// </summary>
    public static class WorldEvents
    {
        public const string Brawl = "brawl", Payphone = "payphone", Television = "television", Dungeon = "dungeon";

        /// <summary>Flat scenes with room for two packs to fight (an open circle of about 7 m).</summary>
        static readonly string[] Roomy = { "knoll", "grove", "camp", "farm", "yard", "ruins", "gap", "hollow", "boulders" };

        public static bool FitsBrawl(ChunkPlan plan) =>
            (plan.Shape.Kind == ShapeKind.Plain || plan.Shape.Kind == ShapeKind.PlateauTop) &&
            System.Array.IndexOf(Roomy, plan.Variant.Id) >= 0;

        /// <summary>Somewhere to stand a payphone or a television: open ground and lanes, not water, cliffs or landmarks.</summary>
        public static bool FitsOddity(ChunkPlan plan) =>
            plan.Ring >= 1 && (plan.Shape.Kind == ShapeKind.Plain || plan.Shape.Kind == ShapeKind.PlateauTop || plan.Shape.Kind == ShapeKind.Maze);

        /// <summary>
        /// Room for a dungeon entrance (the anthill mound, about 11 × 7 m): roomy flat forest scenes. The ring only decides
        /// which kind of dungeon it is (for now there is one: the anthill, in the forest of rings 1 and 2: its chance in
        /// ring 0 is nil, <see cref="WorldRules.DungeonChance"/>). Not a scene with a card pocket in its middle (the
        /// walled yard): the hill would stand over the pocket's walls and they would block its passage.
        /// </summary>
        public static bool FitsDungeon(ChunkPlan plan) => plan.Biome == Biome.Forest && FitsBrawl(plan) && !TileShapes.IsPocketScene(plan.Variant.Id);

        public static string Choose(ChunkPlan plan, int seed, WorldRules rules)
        {
            var c = plan.Coord;
            if (WorldRules.IsStartArea(c)) return null;
            if (FitsDungeon(plan) && Hash.Unit(seed, Salt.Dungeon, c.X, c.Y) < rules.DungeonChance[plan.Ring]) return Dungeon;
            if (FitsBrawl(plan) && Hash.Unit(seed, Salt.Event, c.X, c.Y) < rules.BrawlChance[plan.Ring]) return Brawl;
            if (FitsOddity(plan) && Hash.Unit(seed, Salt.EventKind, c.X, c.Y) < rules.OddityChance[plan.Ring])
                return Hash.Unit(seed, Salt.EventKind + 1, c.X, c.Y) < .5 ? Payphone : Television;
            return null;
        }
    }
}
