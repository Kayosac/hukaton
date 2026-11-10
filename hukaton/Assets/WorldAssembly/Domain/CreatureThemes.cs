using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>A group of one creature kind: "bee-swarm" × 3.</summary>
    public readonly struct Spawn
    {
        public readonly string Creature;
        public readonly int Count;
        public Spawn(string creature, int count) { Creature = creature; Count = count; }
        public override string ToString() => Count > 1 ? $"{Creature}×{Count}" : Creature;
    }

    /// <summary>
    /// Who lives where (Kayospace «Открытый мир v2», существа по месту и кольцу): every kind of place has its own
    /// creatures — bees at the apiary, rats and the rooster on the farm, goblin dogs at the camp, the dwarf robot in
    /// the ruins, bushes and living trees in the woods — calm ones in ring 0, wilder groups further out. Options are
    /// separated by "|", groups by "+", "*n" is a group of n.
    /// </summary>
    public static class CreatureThemes
    {
        sealed class Theme
        {
            public readonly string[][] Tiers;
            public Theme(string calm, string wild, string fierce) { Tiers = new[] { Split(calm), Split(wild), Split(fierce) }; }
            static string[] Split(string options) => string.IsNullOrEmpty(options) ? Array.Empty<string>() : options.Split('|');
        }

        static readonly Dictionary<string, Theme> themes = new Dictionary<string, Theme>
        {
            // Scenes of flat chunks.
            ["apiary"] = new Theme("zombie-beekeeper", "bee-swarm*3|bee-swarm*2+zombie-beekeeper", "enraged-bee-mutant+bee-swarm*2|bee-swarm*4"),
            ["farm"] = new Theme("rooster-baseball*2", "rat-pumpkin*3|rat-pumpkin*2+rooster-baseball", "rat-pumpkin*4|goblin-dog*2+rat-pumpkin*2"),
            ["camp"] = new Theme("kind-little-worm*2", "goblin-dog*2|goblin-dog*3", "goblin-dog*3+drunken-dwarf-robot|goblin-dog*4"),
            ["ruins"] = new Theme("purple-mushroom*2", "drunken-dwarf-robot|goblin-dog*2", "drunken-dwarf-robot*2"),
            ["grove"] = new Theme("teen-leshy-skater", "hostile-bush*2|teen-leshy-skater*2", "sinister-living-tree+hostile-bush*2"),
            ["hollow"] = new Theme("purple-mushroom*3", "purple-mushroom*2+hostile-bush", "budding-troll"),
            ["pond"] = new Theme("snail-frog-mount", "snail-frog-mount*2", "snail-frog-mount+rat-pumpkin*2"),
            ["knoll"] = new Theme("kind-little-worm*2", "goblin-dog*2", "budding-troll"),
            ["rocks"] = new Theme("kind-little-worm*2|purple-mushroom*2", "goblin-dog*2|rat-pumpkin*3", "budding-troll|goblin-dog*3"),
            // Other shapes.
            ["maze"] = new Theme("kind-little-worm", "hostile-bush*2", "hostile-bush*3"),
            ["river"] = new Theme("snail-frog-mount", "rat-pumpkin*2|snail-frog-mount*2", "goblin-dog*2"),
            ["canyon"] = new Theme("", "goblin-dog*2", "goblin-dog*3"),
            ["plateau"] = new Theme("kind-little-worm", "budding-troll", "budding-troll+goblin-dog*2"),
            ["summit"] = new Theme("", "sinister-living-tree|drunken-dwarf-robot", "sinister-living-tree|drunken-dwarf-robot+goblin-dog*2"),
            // Landmarks: their rewards are guarded by these.
            ["fort"] = new Theme("purple-mushroom*2", "drunken-dwarf-robot+goblin-dog", "drunken-dwarf-robot*2"),
            ["crater"] = new Theme("purple-mushroom*3", "budding-troll", "budding-troll+purple-mushroom*2"),
            ["watchhill"] = new Theme("kind-little-worm*2", "goblin-dog*3", "goblin-dog*3+budding-troll"),
            ["beeyard"] = new Theme("zombie-beekeeper*2", "bee-swarm*3+zombie-beekeeper", "enraged-bee-mutant+bee-swarm*3"),
            ["village"] = new Theme("rooster-baseball*2", "rat-pumpkin*3+rooster-baseball", "goblin-dog*3+rat-pumpkin"),
            ["stone-forest"] = new Theme("teen-leshy-skater", "hostile-bush*2", "sinister-living-tree*2"),
            ["mesa"] = new Theme("kind-little-worm*2", "goblin-dog*2+budding-troll|goblin-dog*3", "budding-troll+goblin-dog*2"),
            ["sinkhole"] = new Theme("purple-mushroom*3", "hostile-bush*2+purple-mushroom", "budding-troll+purple-mushroom*2"),
            ["lake"] = new Theme("snail-frog-mount", "snail-frog-mount*2|rat-pumpkin*2", "goblin-dog*2+snail-frog-mount"),
            ["swamp"] = new Theme("snail-frog-mount*2", "snail-frog-mount+rat-pumpkin*2", "budding-troll+snail-frog-mount"),
            ["giant-log"] = new Theme("teen-leshy-skater*2", "hostile-bush*2|teen-leshy-skater*2", "sinister-living-tree"),
        };

        /// <summary>Theme of a scene id; plain rocky scenes share one.</summary>
        public static string ForScene(string scene)
        {
            switch (scene)
            {
                case "gap": case "boulders": case "gully": case "tower": case "thorns": case "pillar": return "rocks";
                case "yard": return "ruins";
                default: return themes.ContainsKey(scene) ? scene : "rocks";
            }
        }

        /// <summary>Creature groups for a theme in a ring (tier 0 calm, 1 wild, 2 fierce), picked by <paramref name="pick"/>.</summary>
        public static List<Spawn> Pick(string theme, int ring, double pick)
        {
            var result = new List<Spawn>();
            if (!themes.TryGetValue(theme, out var t)) return result;
            var options = t.Tiers[Math.Min(ring, 2)];
            if (options.Length == 0) return result;
            string option = options[Math.Min(options.Length - 1, (int)(pick * options.Length))];
            foreach (var group in option.Split('+'))
            {
                int star = group.IndexOf('*');
                result.Add(star < 0 ? new Spawn(group, 1) : new Spawn(group.Substring(0, star), int.Parse(group.Substring(star + 1))));
            }
            return result;
        }

        /// <summary>Every creature id any theme names (the content asset must know them all).</summary>
        public static IEnumerable<string> AllCreatures()
        {
            var seen = new HashSet<string>();
            foreach (var t in themes.Values)
                foreach (var tier in t.Tiers)
                    foreach (var option in tier)
                        foreach (var group in option.Split('+'))
                        {
                            int star = group.IndexOf('*');
                            string id = star < 0 ? group : group.Substring(0, star);
                            if (seen.Add(id)) yield return id;
                        }
        }

        /// <summary>Calm creatures: the only ones ring 0 may hold.</summary>
        public static bool IsCalm(string creature) =>
            creature == "purple-mushroom" || creature == "kind-little-worm" || creature == "rooster-baseball" ||
            creature == "snail-frog-mount" || creature == "teen-leshy-skater" || creature == "zombie-beekeeper";
    }
}
