using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.Decks.Unity;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using Hakaton.Loot;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Dungeons.EditorTools
{
    /// <summary>
    /// One-off (delete before delivery): the anthill's loot and trophy cards (pg_anthill-loot-cards). Bridge:
    /// <c>invoke Hakaton.Dungeons.EditorTools.AnthillLootCards.Chests</c> — the anthill's chests with its own curios;
    /// <c>…Cards</c> — «Вербовка» and «Эхолот»: art import, sounds, materials, the wave, effects, definitions, config.
    /// Run <c>Hakaton.Loot.EditorTools.CurioSetup.Run</c> first (the curio items).
    /// </summary>
    static class AnthillLootCards
    {
        const string KitPath = "Assets/Dungeons/Anthill/Anthill Kit.asset";
        const string Folder = "Assets/Dungeons/Anthill";

        static LootItem Item(string id) => AssetDatabase.LoadAssetAtPath<LootItem>($"Assets/Loot/Items/{id}.asset");

        static LootDrop Drop(string id, float chance) => new LootDrop { item = Item(id), chance = chance, min = 1, max = 1 };

        /// <summary>
        /// Store chests (Sturdy, half the reward spots) hold the button drum or the inverted farm now and then; stash
        /// chests (Rich) the farm and often the drum, now and then the amber or the jelly; the prize chest (now rank 5)
        /// nearly always the amber and the jelly, mostly the farm and the drum too, sometimes the petrified egg.
        /// </summary>
        public static string Chests(string arg)
        {
            var log = new StringBuilder();
            foreach (var id in new[] { "ant_button_drum", "ant_inverted_farm", "ant_amber_fly", "ant_royal_jelly", "ant_fossil_queen_egg" })
                if (!Item(id)) return "FAILED no item " + id + " (run CurioSetup.Run first)";

            var prize = AssetDatabase.LoadAssetAtPath<ChestTier>($"{Folder}/Anthill Prize Tier.asset");
            prize.rank = 5;
            prize.bonus = new LootTable
            {
                atLeastOne = true, minimumDrops = 1,
                entries = new[] { Drop("ant_amber_fly", .95f), Drop("ant_royal_jelly", .95f), Drop("ant_fossil_queen_egg", .15f),
                    Drop("ant_inverted_farm", .8f), Drop("ant_button_drum", .8f) },
            };
            EditorUtility.SetDirty(prize);

            var store = Tier("Chest 3 Sturdy", "Anthill Store Tier", "Кладовая муравейника", new LootTable
            {
                atLeastOne = false, minimumDrops = 1,
                entries = new[] { Drop("ant_button_drum", .4f), Drop("ant_inverted_farm", .15f) },
            });
            var stash = Tier("Chest 4 Rich", "Anthill Stash Tier", "Тайник муравейника", new LootTable
            {
                atLeastOne = true, minimumDrops = 1,
                entries = new[] { Drop("ant_inverted_farm", .85f), Drop("ant_amber_fly", .3f), Drop("ant_royal_jelly", .3f), Drop("ant_button_drum", .6f) },
            });
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            kit.sideChest = Chest("Chest 3 Sturdy", "Anthill Store Chest", store);
            kit.stashChest = Chest("Chest 4 Rich", "Anthill Stash Chest", stash);
            // Tuned with the chests above (Monte Carlo over small and large anthills, 1–2 stashes): about 7 curios a
            // dungeon, 5–10 in nine anthills of ten.
            kit.sideChestChance = .5f;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            foreach (var tier in new[] { prize, store, stash })
                log.AppendLine($"{tier.name} rank {tier.rank}: " + string.Join(", ", tier.bonus.entries.Select(e => $"{e.item.id} {e.chance}")));
            log.AppendLine($"kit side {kit.sideChest.name} stash {kit.stashChest.name} prize {kit.prizeChest.name}");
            return log.ToString();
        }

        static ChestTier Tier(string source, string name, string title, LootTable bonus)
        {
            string path = $"{Folder}/{name}.asset";
            if (!AssetDatabase.LoadAssetAtPath<ChestTier>(path)) AssetDatabase.CopyAsset($"Assets/Game/Data/Chests/{source}.asset", path);
            var tier = AssetDatabase.LoadAssetAtPath<ChestTier>(path);
            tier.displayName = title;
            tier.bonus = bonus;
            EditorUtility.SetDirty(tier);
            return tier;
        }

        static GameObject Chest(string source, string name, ChestTier tier)
        {
            string path = $"{Folder}/{name}.prefab";
            var basis = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Expedition/Prefabs/Chests/{source}.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basis);
            instance.GetComponent<TreasureChest>().tier = tier;
            instance.name = name;
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return saved;
        }

        // ---------------- cards ----------------

        const string CardFolder = "Assets/Game/Prefabs/Cards/Sonar";

        public static string Cards(string arg)
        {
            var log = new StringBuilder();
            var recruitArt = Art("Assets/Decks/Art/recruitment.png");
            var sonarArt = Art("Assets/Decks/Art/sonar.png");
            if (!recruitArt || !sonarArt) return "FAILED card art";

            var ping = Cue("sonar_ping", "Assets/Audio/Sfx/cards/sonar_ping_01.wav", .75f, 0, 2, 40);
            var blip = Cue("sonar_blip", "Assets/Audio/Sfx/cards/sonar_blip_01.wav", .5f, .85f, 3, 70);
            var chime = Cue("recruit_chime", "Assets/Audio/Sfx/cards/recruit_chime_01.wav", .6f, .85f, 2, 40);

            var shader = Shader.Find("Hakaton/Cards/Sonar silhouette");
            if (!shader) return "FAILED no sonar shader";
            var silhouette = Mat($"{CardFolder}/Sonar silhouette.mat", shader, 1.5f, 1, .32f);
            var ringMat = Mat($"{CardFolder}/Sonar ring.mat", shader, 1.8f, .45f, .95f);
            var wave = Wave(ringMat);

            var heal = AssetDatabase.LoadAssetAtPath<CardEffect>("Assets/Game/Data/Effects/Heal.asset");
            var lure = AssetDatabase.LoadAssetAtPath<CardEffect>("Assets/Game/Data/Effects/Lure.asset");
            var discord = AssetDatabase.LoadAssetAtPath<CardEffect>("Assets/Game/Data/Effects/Discord.asset");

            var recruit = Asset<RecruitmentEffect>("Assets/Game/Data/Effects/Recruitment.asset");
            recruit.impactVfx = discord.impactVfx;
            recruit.vfxScale = 1;
            recruit.impactSound = chime;
            recruit.missSound = discord.missSound;
            recruit.shake = .1f;
            recruit.hitShake = .22f;
            recruit.recruits = new[] { "AntWorker", "AntPlug", "AntMachineGunCrew", "BubbleLarva", "WingedAnt" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/{n}.prefab").GetComponent<WorldCreature>()).ToArray();
            recruit.holdGround = new[] { AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/AntMachineGunCrew.prefab").GetComponent<WorldCreature>() };
            recruit.recruitBurst = heal.impactVfx;
            recruit.recruitSound = chime;
            EditorUtility.SetDirty(recruit);

            var sonar = Asset<SonarEffect>("Assets/Game/Data/Effects/Sonar.asset");
            sonar.impactVfx = lure.impactVfx;
            sonar.vfxScale = 1.2f;
            sonar.impactSound = ping;
            sonar.missSound = null;
            sonar.shake = .05f;
            sonar.hitShake = .12f;
            sonar.wavePrefab = wave;
            sonar.silhouette = silhouette;
            sonar.foundSound = blip;
            EditorUtility.SetDirty(sonar);

            var recruitCard = Card("recruitment", "Вербовка", CardAction.Recruitment, recruitArt, new Color(.74f, .2f, .24f), recruit,
                "Муравьи в зоне переходят на твою сторону до конца вылазки.", 4);
            var sonarCard = Card("sonar", "Эхолот", CardAction.Sonar, sonarArt, new Color(.3f, .86f, .84f), sonar,
                "Звуковая волна находит врагов вокруг и подсвечивает их сквозь стены.", 3);

            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            config.trophyCards = new[] { recruitCard, sonarCard };
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            log.AppendLine($"cards: {recruitCard.id} ({recruitArt.texture.width}x{recruitArt.texture.height}), {sonarCard.id}; config trophyCards {config.trophyCards.Length}");
            log.AppendLine($"recruits: {string.Join(", ", recruit.recruits.Select(r => r ? r.displayName : "MISSING"))}; hold: {recruit.holdGround[0].displayName}");
            log.AppendLine($"vfx: recruit {(recruit.recruitBurst ? recruit.recruitBurst.name : "none")}, sonar {(sonar.impactVfx ? sonar.impactVfx.name : "none")}");
            return log.ToString();
        }

        static Sprite Art(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var reference = (TextureImporter)AssetImporter.GetAtPath("Assets/Decks/Art/crows.png");
            if (!importer || !reference) return null;
            var settings = new TextureImporterSettings();
            reference.ReadTextureSettings(settings);
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(reference.GetDefaultPlatformTextureSettings());
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static SfxCue Cue(string name, string clipPath, float volume, float spatial, float min, float max)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            var cue = Asset<SfxCue>($"Assets/Audio/Cues/{name}.asset");
            cue.clips = new[] { clip };
            cue.volume = volume;
            cue.pitch = new Vector2(.98f, 1.02f);
            cue.spatialBlend = spatial;
            cue.minDistance = min;
            cue.maxDistance = max;
            cue.cooldown = .03f;
            EditorUtility.SetDirty(cue);
            return cue;
        }

        static Material Mat(string path, Shader shader, float glow, float hidden, float open)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_Tint", Color.white);
            material.SetFloat("_VertexColour", 1);
            material.SetFloat("_Glow", glow);
            material.SetFloat("_Hidden", hidden);
            material.SetFloat("_Open", open);
            EditorUtility.SetDirty(material);
            return material;
        }

        static SonarWave Wave(Material material)
        {
            string path = $"{CardFolder}/Sonar wave.prefab";
            var root = new GameObject("Sonar wave");
            var line = root.AddComponent<LineRenderer>();
            var echoGo = new GameObject("Echo");
            echoGo.transform.SetParent(root.transform, false);
            var echo = echoGo.AddComponent<LineRenderer>();
            foreach (var ring in new[] { line, echo })
            {
                ring.sharedMaterial = material;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.receiveShadows = false;
                ring.loop = true;
                ring.useWorldSpace = false;
                ring.numCornerVertices = 1;
                ring.numCapVertices = 0;
                ring.positionCount = 0;
                ring.textureMode = LineTextureMode.Stretch;
            }
            var wave = root.AddComponent<SonarWave>();
            wave.line = line;
            wave.echo = echo;
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<SonarWave>();
        }

        static CardDefinition Card(string id, string title, CardAction action, Sprite art, Color accent, ScriptableObject effect, string text, int rank)
        {
            var card = Asset<CardDefinition>($"Assets/Game/Data/{id}.asset");
            card.id = id;
            card.displayName = title;
            card.action = action;
            card.artwork = art;
            card.accent = accent;
            card.effectAsset = effect;
            card.description = text;
            card.purchasePrice = 0;
            card.rank = rank;
            card.dummy = false;
            card.upgradable = true;
            card.uses = LevelInt.All(1);
            EditorUtility.SetDirty(card);
            return card;
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
