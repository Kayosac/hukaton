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
    /// separated by "|", groups by "+", "*n" is a group of n, "*n-m" a group of n to m.
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
            ["hollow"] = new Theme("purple-mushroom*3", "purple-mushroom*2+hostile-bush" + "|" + Agaric1, "budding-troll" + "|" + Agaric2),
            ["pond"] = new Theme("snail-frog-mount", "snail-frog-mount*2", "snail-frog-mount+rat-pumpkin*2"),
            ["knoll"] = new Theme("kind-little-worm*2", "goblin-dog*2", "budding-troll"),
            ["rocks"] = new Theme("kind-little-worm*2|purple-mushroom*2", "goblin-dog*2|rat-pumpkin*3", "budding-troll|goblin-dog*3"),
            // Other shapes.
            ["maze"] = new Theme("kind-little-worm", "hostile-bush*2", "hostile-bush*3"),
            ["forest"] = new Theme("teen-leshy-skater", "hostile-bush*2|teen-leshy-skater*2|sinister-living-tree", "sinister-living-tree+hostile-bush*2|sinister-living-tree*2"),
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
            ["sinkhole"] = new Theme("purple-mushroom*3", "hostile-bush*2+purple-mushroom" + "|" + Agaric1, "budding-troll+purple-mushroom*2" + "|" + Agaric2),
            ["lake"] = new Theme("snail-frog-mount", "snail-frog-mount*2|rat-pumpkin*2", "goblin-dog*2+snail-frog-mount"),
            ["flooded-village"] = new Theme("rooster-baseball*2", "rat-pumpkin*3+snail-frog-mount", "goblin-dog*3+rat-pumpkin"),
            ["swamp"] = new Theme("snail-frog-mount*2", "snail-frog-mount+rat-pumpkin*2", "budding-troll+snail-frog-mount"),
            ["giant-log"] = new Theme("teen-leshy-skater*2", "hostile-bush*2|teen-leshy-skater*2", "sinister-living-tree"),
            // The rotten swamp: the creatures of the place each scene is most like, and the swamp's own.
            // Giant dung beetles patrol the dry ground in squads: 3–4 in ring 1, 5–7 further out.
            // Mantis men lie in wait in the reeds: one in ring 1, two further out.
            ["swamp-hummocks"] = new Theme("snail-frog-mount*2" + "|" + Drowned, "snail-frog-mount+rat-pumpkin*2|" + Beetles1 + "|" + Mantis1 + "|" + Leeches1 + "|" + Toad1 + "|" + Agaric1 + "|" + Mosquitoes1, "budding-troll+snail-frog-mount|" + Beetles2 + "|" + Mantis2 + "|" + Leeches2 + "|" + Toad2 + "|" + Agaric2 + "|" + Mosquitoes2),
            ["swamp-log-road"] = new Theme("snail-frog-mount" + "|" + Drowned, "rat-pumpkin*2|snail-frog-mount*2|" + Beetles1 + "|" + Mantis1 + "|" + Mutant1 + "|" + Leeches1 + "|" + Mosquitoes1, "goblin-dog*2|" + Beetles2 + "|" + Mantis2 + "|" + Mutant2 + "|" + Leeches2 + "|" + Mosquitoes2),
            ["swamp-ruins"] = new Theme("purple-mushroom*2" + "|" + Drowned, "drunken-dwarf-robot|goblin-dog*2|" + Beetles1 + "|" + Mantis1 + "|" + Mutant1 + "|" + Leeches1 + "|" + Spider1 + "|" + Mosquitoes1 + "|" + Ghost1, "drunken-dwarf-robot*2|" + Beetles2 + "|" + Mantis2 + "|" + Mutant2 + "|" + Leeches2 + "|" + Spider2 + "|" + Mosquitoes2 + "|" + Ghost2),
            ["swamp-graveyard"] = new Theme("purple-mushroom*2" + "|" + Drowned, "drunken-dwarf-robot|goblin-dog*2|" + Beetles1 + "|" + Mantis1 + "|" + Mutant1 + "|" + Leeches1 + "|" + Spiders1 + "|" + Agaric1 + "|" + Mosquitoes1 + "|" + Ghosts1, "drunken-dwarf-robot*2|" + Beetles2 + "|" + Mantis2 + "|" + Mutant2 + "|" + Leeches2 + "|" + Spiders2 + "|" + Agaric2 + "|" + Mosquitoes2 + "|" + Ghosts2),
            ["swamp-tar-pit"] = new Theme("purple-mushroom*3" + "|" + Drowned, "purple-mushroom*2+hostile-bush|" + Beetles1 + "|" + Mantis1 + "|" + Leeches1 + "|" + Spider1 + "|" + Mosquitoes1, "budding-troll|" + Beetles2 + "|" + Mantis2 + "|" + Leeches2 + "|" + Spider2 + "|" + Mosquitoes2),
            ["swamp-bog"] = new Theme("snail-frog-mount*2" + "|" + Drowned, "snail-frog-mount+rat-pumpkin*2|" + Mantis1 + "|" + Leeches1 + "|" + Toad1 + "|" + Mosquitoes1, "budding-troll+snail-frog-mount|" + Mantis2 + "|" + Leeches2 + "|" + Toad2 + "|" + Mosquitoes2),
            ["swamp-isle"] = new Theme("snail-frog-mount" + "|" + Drowned, "snail-frog-mount*2|" + Mantis1 + "|" + Leeches1 + "|" + Spider1 + "|" + Toad1 + "|" + Agaric1 + "|" + Mosquitoes1, "snail-frog-mount+rat-pumpkin*2|" + Mantis2 + "|" + Leeches2 + "|" + Spider2 + "|" + Toad2 + "|" + Agaric2 + "|" + Mosquitoes2),
            ["swamp-mill"] = new Theme("rooster-baseball*2" + "|" + Drowned, "rat-pumpkin*3|rat-pumpkin*2+rooster-baseball|" + Beetles1 + "|" + Mutant1 + "|" + Leeches1 + "|" + Mosquitoes1 + "|" + Ghost1, "rat-pumpkin*4|goblin-dog*2+rat-pumpkin*2|" + Beetles2 + "|" + Mutant2 + "|" + Leeches2 + "|" + Mosquitoes2 + "|" + Ghost2),
        };

        const string Beetles1 = "giant-dung-beetle*3-4", Beetles2 = "giant-dung-beetle*5-7";
        const string Mantis1 = "mantis-man", Mantis2 = "mantis-man*2";
        // Swamp mutants haunt the huts, the mill, the graveyard and the log road: one, further out one or two.
        const string Mutant1 = "swamp-mutant", Mutant2 = "swamp-mutant*1-2";
        // Giant leeches lie under the water of every part of the swamp: one or two, further out two or three.
        const string Leeches1 = "giant-swamp-leech*1-2", Leeches2 = "giant-swamp-leech*2-3";
        // Giant swamp spiders hunt round snags, tar and huts: one, further out two; on the drowned graveyard they swarm.
        const string Spider1 = "giant-swamp-spider", Spider2 = "giant-swamp-spider*2";
        const string Spiders1 = "giant-swamp-spider*3", Spiders2 = "giant-swamp-spider*3-5";
        // Giant vile toads squat on the wet ground: hummocks, the bog, the snag isle; one, further out one or two.
        const string Toad1 = "giant-vile-toad", Toad2 = "giant-vile-toad*1-2";
        // Raging fly agarics root among the mushrooms: hummocks, the graveyard, the snag isle, and the forest's hollows
        // and sinkholes; one, further out one or two.
        const string Agaric1 = "raging-fly-agaric", Agaric2 = "raging-fly-agaric*1-2";
        // Mosquito swarms hang over the whole swamp, many at once: three to six, further out six to nine.
        const string Mosquitoes1 = "mosquito-swarm*3-6", Mosquitoes2 = "mosquito-swarm*6-9";
        // Drowned swimmers drift in the water of every part of the swamp, from the start ring on (calm: thrice as many).
        const string Drowned = "drowned-swimmer*1-2";
        // Sheet ghosts haunt the sunken hut and the mill (one, further out one or two) and the graveyard (two, then two or three).
        const string Ghost1 = "sheet-ghost", Ghost2 = "sheet-ghost*1-2", Ghosts1 = "sheet-ghost*2", Ghosts2 = "sheet-ghost*2-3";

        /// <summary>Theme of a scene id; plain rocky scenes share one.</summary>
        public static string ForScene(string scene)
        {
            switch (scene)
            {
                case "gap": case "boulders": case "gully": case "tower": case "thorns": case "pillar": return "rocks";
                case "yard": return "ruins";
                // The rotten swamp keeps the creatures of the places it is most like, and adds its own.
                case "hummocks": return "swamp-hummocks";
                case "breathing-bog": case "eternal-flame": return "swamp-bog";
                case "log-road": return "swamp-log-road";
                case "sunken-hut": return "swamp-ruins";
                case "drowned-graveyard": return "swamp-graveyard";
                case "tar-pit": return "swamp-tar-pit";
                case "drowned-mill": return "swamp-mill";
                case "deadwood-isle": return "swamp-isle";
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
            double scaled = pick * options.Length;
            int index = Math.Min(options.Length - 1, (int)scaled);
            // What is left of the pick inside the chosen option sizes the groups given as "n-m".
            double rest = Math.Max(0, Math.Min(.999999, scaled - index));
            foreach (var group in options[index].Split('+'))
            {
                int star = group.IndexOf('*');
                result.Add(star < 0 ? new Spawn(group, 1) : new Spawn(group.Substring(0, star), Count(group.Substring(star + 1), rest)));
            }
            return result;
        }

        /// <summary>"3" is three; "3-4" is three or four, picked by <paramref name="pick"/> (0..1).</summary>
        static int Count(string text, double pick)
        {
            int dash = text.IndexOf('-');
            if (dash < 0) return int.Parse(text);
            int low = int.Parse(text.Substring(0, dash)), high = int.Parse(text.Substring(dash + 1));
            if (high < low) (low, high) = (high, low);
            return low + Math.Min(high - low, (int)(pick * (high - low + 1)));
        }

        /// <summary>Lairs (ruins, hollows and the swamp's like them) are busier than plain ground.</summary>
        public static bool IsLair(string theme) => theme == "ruins" || theme == "hollow" || theme == "swamp-ruins" || theme == "swamp-graveyard" || theme == "swamp-tar-pit";

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

        /// <summary>Calm creatures — the peaceful and the timid ones: the only ones ring 0 may hold.</summary>
        public static bool IsCalm(string creature) =>
            creature == "purple-mushroom" || creature == "kind-little-worm" || creature == "rooster-baseball" ||
            creature == "snail-frog-mount" || creature == "teen-leshy-skater" || creature == "zombie-beekeeper" ||
            creature == "drowned-swimmer";
    }
}
