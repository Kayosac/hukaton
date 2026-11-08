using System;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.Trading.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using static Hakaton.Authoring.Kit;

namespace Hakaton.Authoring
{
    /// <summary>
    /// Swaps the generated effects for Retro Arsenal ones: prefab variants (audio off — sounds stay in SfxPlayer cues,
    /// VfxBurst added) under Game/Prefabs/Effects/Retro, wired into cards, shared expedition effects, obstacles, loot and
    /// the shop. Temporary authoring script.
    /// </summary>
    static class ReworkRetro
    {
        const string Source = "Assets/Plugins/Retro Arsenal/Prefabs/";
        const string Folder = "Assets/Game/Prefabs/Effects/Retro";

        public static string Run()
        {
            Log.Clear();
            try
            {
                EnsureFolder(Folder);
                var fire = Burst("Fire explosion", "Combat/Explosions/Fire/FireExplosion.prefab");
                var frost = Burst("Frost explosion", "Combat/Explosions/Frost/FrostExplosion.prefab");
                var storm = Burst("Impulse blast", "Combat/Explosions/Storm/StormExplosionBlue.prefab");
                var punch = Burst("Strike hit", "Combat/Fighting/Punch/PunchYellow.prefab");
                var heavy = Burst("Heavy hit", "Combat/Fighting/Punch2/Punch2Yellow.prefab");
                var heal = Burst("Heal poof", "Interactive/Symbols/Healing/HealBigPoof.prefab");
                var fizzle = Burst("Card fizzle", "Combat/Fighting/Hit/AttackHitRed.prefab");
                var burn = Burst("Card burn", "Combat/Explosions/BasicTiny/BasicTinyExplosionRed.prefab");
                var pickup = Burst("Pickup stars", "Interactive/Symbols/Star/StarBurst.prefab");
                var death = Burst("Creature death", "Combat/Death/Skull/DeathTiny.prefab");
                var dust = Burst("Dust puff", "Interactive/Movement/JumpCloudSmall.prefab");
                var landing = Burst("Landing dust", "Interactive/Movement/JumpCloud.prefab");
                var home = Burst("Return home", "Environment/Sparkle/Collapse/SparkleCollapseYellow.prefab");
                var stunned = Burst("Stunned stars", "Combat/Fighting/Stunned/StunnedStars.prefab", loop: true);
                var flames = Burst("Burning thorns", "Environment/Fire/FieldWide/FireFieldWide.prefab", loop: true);
                var coins = Burst("Sale coins", "Interactive/Money/Coins/CoinGoldBurst.prefab");
                var coinFountain = Burst("Big sale coins", "Interactive/Money/Coins/CoinGoldFountain.prefab");
                var happy = Burst("Happy emote", "Interactive/Emojis/Joy/Grinning.prefab");
                var angry = Burst("Angry emote", "Interactive/Emojis/Anger/Angry.prefab");
                var lootRay = Plain("Loot ray", "Interactive/Loot/Obtainables/Lootray/Mini/LootRayMiniYellow.prefab");

                Effect("Fire", fire); Effect("Ice", frost); Effect("Impulse", storm);
                Effect("Strike", punch); Effect("Heavy strike", heavy); Effect("Heal", heal);

                var shared = Load<ExpeditionVfx>("Assets/Game/Data/Expedition Effects.asset");
                shared.paperPoof = fizzle; shared.cardBurn = burn; shared.pickupSparkle = pickup; shared.creatureDeath = death;
                shared.dustPuff = dust; shared.landingDust = landing; shared.returnPoof = home; shared.stunned = stunned;
                EditorUtility.SetDirty(shared);

                LootGlints(lootRay);
                Scene(fire, frost, flames, coins, coinFountain, happy, angry);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Say("FAILED " + e); }
            return Log.ToString();
        }

        /// <summary>A one-shot (or held, when looping) effect variant with VfxBurst.</summary>
        static VfxBurst Burst(string name, string source, bool loop = false)
        {
            return Make(name, source, instance =>
            {
                var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                if (loop)
                    foreach (var system in systems) { var main = system.main; main.loop = true; }
                var roots = systems.Where(s => !s.transform.parent || !s.transform.parent.GetComponentInParent<ParticleSystem>(true)).ToArray();
                float lifetime = loop ? 2 : systems.Max(s => s.main.startDelay.constantMax + s.main.duration + s.main.startLifetime.constantMax);
                var burst = GetOrAdd<VfxBurst>(instance);
                burst.systems = roots;
                burst.tinted = Array.Empty<ParticleSystem>();
                burst.lifetime = Mathf.Clamp(lifetime + .2f, .5f, 6f);
            }).GetComponent<VfxBurst>();
        }

        /// <summary>A plain variant (audio off) for looping attachments such as the loot ray.</summary>
        static GameObject Plain(string name, string source) => Make(name, source, _ => { });

        static GameObject Make(string name, string source, Action<GameObject> setup)
        {
            var prefab = Load<GameObject>(Source + source);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                instance.name = name;
                // One sound pipeline: the game's SfxPlayer cues; the pack's own sources stay silent.
                foreach (var audio in instance.GetComponentsInChildren<AudioSource>(true)) { audio.playOnAwake = false; audio.enabled = false; }
                setup(instance);
                string path = $"{Folder}/{name}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                Say($"variant {path} <- {source}");
                return saved;
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static void Effect(string asset, VfxBurst vfx)
        {
            var effect = Load<CardEffect>($"Assets/Game/Data/Effects/{asset}.asset");
            effect.impactVfx = vfx;
            EditorUtility.SetDirty(effect);
            Say($"card effect {asset} -> {vfx.name}");
        }

        static void LootGlints(GameObject lootRay)
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs", "Assets/Expedition/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<WorldLoot>())
                .ToArray();
            foreach (var path in paths)
                EditPrefab(path, root =>
                {
                    var loot = root.GetComponent<WorldLoot>();
                    var glint = Find(root.transform, "Glint");
                    bool own = glint && (!PrefabUtility.IsPartOfVariantPrefab(root) || PrefabUtility.IsAddedGameObjectOverride(glint.gameObject)
                                         || PrefabUtility.GetOutermostPrefabInstanceRoot(glint.gameObject) == glint.gameObject);
                    if (glint && !own) { Say($"loot {path}: glint inherited"); return; }
                    if (glint) Object.DestroyImmediate(glint.gameObject);
                    var ray = (GameObject)PrefabUtility.InstantiatePrefab(lootRay, root.transform);
                    ray.name = "Glint";
                    ray.transform.localPosition = Vector3.zero;
                    ray.transform.localRotation = Quaternion.identity;
                    loot.glint = ray.GetComponentInChildren<ParticleSystem>();
                    Say($"loot {path}: loot ray");
                });
        }

        public static string Confetti()
        {
            Log.Clear();
            var confetti = Burst("Goal confetti", "Interactive/Confetti/ConfettiExplosion.prefab");
            var loop = Object.FindAnyObjectByType<Hakaton.Game.GameLoop>(FindObjectsInactive.Include);
            loop.goalConfetti = confetti;
            Record(loop);
            EditorSceneManager.MarkSceneDirty(loop.gameObject.scene);
            EditorSceneManager.SaveScene(loop.gameObject.scene);
            return Log.ToString() + " wired";
        }

        /// <summary>Removes every leftover instance of the old generated glint from loot prefabs and the scene.</summary>
        public static string CleanOldGlints()
        {
            Log.Clear();
            var old = Load<GameObject>("Assets/Game/Prefabs/Effects/Loot glint.prefab");
            bool IsOld(Transform t) => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)
                                       && PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject) == old;
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs", "Assets/Expedition/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<WorldLoot>()).ToArray();
            foreach (var path in paths)
                EditPrefab(path, root =>
                {
                    var stale = root.GetComponentsInChildren<Transform>(true).Where(IsOld).ToArray();
                    foreach (var t in stale) Object.DestroyImmediate(t.gameObject);
                    Say($"{path}: removed {stale.Length}, glint={(root.GetComponent<WorldLoot>().glint ? root.GetComponent<WorldLoot>().glint.name : "none")}");
                });
            var scene = EditorSceneManager.GetActiveScene();
            var inScene = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(IsOld).ToArray();
            foreach (var t in inScene) Object.DestroyImmediate(t.gameObject);
            Say("scene: removed " + inScene.Length);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        static void Scene(VfxBurst fire, VfxBurst frost, VfxBurst flames, VfxBurst coins, VfxBurst coinFountain, VfxBurst happy, VfxBurst angry)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var obstacle in Object.FindObjectsByType<WorldObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool lake = obstacle.kind == ObstacleKind.Lake;
                obstacle.reactVfx = lake ? frost : fire;
                obstacle.loopVfx = lake ? null : flames;
                Record(obstacle);
                Say($"obstacle {obstacle.name} -> {obstacle.reactVfx.name}");
            }
            var feel = Object.FindAnyObjectByType<ShopFeel>(FindObjectsInactive.Include);
            var burstObject = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "Coin burst" || t.name == "Sale anchor");
            if (burstObject && burstObject.name == "Coin burst")
            {
                var anchor = new GameObject("Sale anchor").transform;
                anchor.SetParent(burstObject.parent, false);
                anchor.position = burstObject.position;
                Object.DestroyImmediate(burstObject.gameObject);
                burstObject = anchor;
            }
            feel.saleAnchor = burstObject;
            feel.saleBurst = coins; feel.bigSaleBurst = coinFountain;
            feel.happyEmote = happy; feel.angryEmote = angry;
            Record(feel);
            Say("shop feel effects wired, sale anchor " + (burstObject ? burstObject.name : "missing"));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void Record(Component component)
        {
            EditorUtility.SetDirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
