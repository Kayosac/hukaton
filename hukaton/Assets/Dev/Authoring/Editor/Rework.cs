using System;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.Feel;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Hakaton.Authoring.Kit;
using Object = UnityEngine.Object;

namespace Hakaton.Authoring
{
    /// <summary>
    /// One-off authoring pass for the expedition feel rework. Invoked through the Editor bridge:
    /// <c>invoke Hakaton.Authoring.Rework.Run</c>. Produces saved assets, prefabs and scene changes; the script
    /// is deleted afterwards.
    /// </summary>
    static class Rework
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        const string PlayerPrefab = "Assets/Game/Prefabs/Player.prefab";
        const string UiFolder = "Assets/Game/Prefabs/UI/";

        public static string Run()
        {
            Log.Clear();
            try
            {
                Layers();
                ReworkVisuals.Textures();
                var vfx = ReworkVisuals.Effects(out var impulse, out var ice, out var fire, out var strike, out var heavy, out var heal, out var glint);
                ReworkVisuals.Cards(out var projectile, out var debris);
                CardEffects(impulse, ice, fire, strike, heavy, heal);
                var hand = PlayerRig();
                Creatures();
                Loot(glint);
                var hud = BuildHud();
                var fade = BuildFade();
                Scene(vfx, projectile, debris, hud, fade, ice, fire);
                AssetDatabase.SaveAssets();
                Say("DONE");
            }
            catch (Exception exception) { Say("FAILED " + exception); }
            return Log.ToString();
        }

        // ---------------- project settings ----------------

        static void Layers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers.GetArrayElementAtIndex(8).stringValue != "Cards")
            {
                layers.GetArrayElementAtIndex(8).stringValue = "Cards";
                tagManager.ApplyModifiedProperties();
            }
            // Cards never collide physically (they sweep); the player falls through water, cards land on it.
            Physics.IgnoreLayerCollision(8, 8, true);
            Physics.IgnoreLayerCollision(8, 2, true);
            Physics.IgnoreLayerCollision(4, 2, true);
            var dynamics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            dynamics.Update();
            EditorUtility.SetDirty(dynamics.targetObject);
            Say("layers: Cards=8; Water(4) ignores Ignore Raycast(2)");
        }

        // ---------------- cards ----------------

        static void CardEffects(VfxBurst impulse, VfxBurst ice, VfxBurst fire, VfxBurst strike, VfxBurst heavy, VfxBurst heal)
        {
            const string folder = "Assets/Game/Data/Effects/";
            var impulseEffect = Asset<ImpulseEffect>(folder + "Impulse.asset");
            impulseEffect.impactVfx = impulse; impulseEffect.shake = .22f; impulseEffect.hitShake = .3f; impulseEffect.hitStop = .03f;
            var freeze = Asset<FreezeEffect>(folder + "Ice.asset");
            freeze.impactVfx = ice; freeze.shake = .1f; freeze.hitShake = .25f; freeze.hitStop = .05f;
            var burn = Asset<FireEffect>(folder + "Fire.asset");
            burn.impactVfx = fire; burn.shake = .14f; burn.hitShake = .28f; burn.hitStop = .04f;
            var hit = Asset<StrikeEffect>(folder + "Strike.asset");
            hit.impactVfx = strike; hit.shake = .06f; hit.hitShake = .28f; hit.hitStop = .07f; hit.vfxScale = .9f;
            var slam = Asset<HeavyStrikeEffect>(folder + "Heavy strike.asset");
            slam.impactVfx = heavy; slam.shake = .12f; slam.hitShake = .45f; slam.hitStop = .11f;
            var mend = Asset<HealEffect>(folder + "Heal.asset");
            mend.impactVfx = heal; mend.shake = 0; mend.hitShake = .08f;
            foreach (var effect in new CardEffect[] { impulseEffect, freeze, burn, hit, slam, mend }) EditorUtility.SetDirty(effect);

            void Card(string id, CardEffect effect, Color accent)
            {
                var definition = Load<CardDefinition>("Assets/Game/Data/" + id + ".asset");
                definition.effectAsset = effect;
                definition.accent = accent;
                EditorUtility.SetDirty(definition);
            }
            Card("impulse", impulseEffect, new Color(1f, .86f, .42f));
            Card("ice", freeze, new Color(.55f, .88f, 1f));
            Card("fire", burn, new Color(1f, .52f, .18f));
            Card("strike", hit, new Color(.95f, .32f, .26f));
            Card("heavy_strike", slam, new Color(.85f, .5f, .28f));
            Card("heal", mend, new Color(.45f, .92f, .5f));
            Say("card effects assigned");
        }

        // ---------------- player ----------------

        static CardHandView PlayerRig()
        {
            CardHandView result = null;
            EditPrefab(PlayerPrefab, root =>
            {
                var camera = root.GetComponentInChildren<Camera>(true);
                var handRoot = Find(root.transform, "Cards in hand");
                if (!handRoot) handRoot = Child(camera.transform, "Cards in hand").transform;
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(handRoot.gameObject);
                DestroyChildren(handRoot);
                handRoot.localPosition = Vector3.zero;
                handRoot.localRotation = Quaternion.identity;
                var hand = GetOrAdd<CardHandView>(handRoot.gameObject);
                hand.slots = Enumerable.Range(1, 5).Select(i => { var v = ReworkVisuals.HandQuad(handRoot, "Card slot " + i); v.gameObject.SetActive(false); return v; }).ToArray();
                hand.stack = Enumerable.Range(1, 3).Select(i => { var v = ReworkVisuals.HandQuad(handRoot, "Other deck " + i); v.gameObject.SetActive(false); return v; }).ToArray();
                hand.explorationBack = Load<Texture2D>("Assets/Decks/Art/Backs/card_back_exploration.png");
                hand.combatBack = Load<Texture2D>("Assets/Decks/Art/Backs/card_back_combat.png");

                var socket = Find(camera.transform, "Card launch socket");
                if (!socket) socket = Child(camera.transform, "Card launch socket").transform;
                socket.localPosition = new Vector3(.1f, -.12f, .42f);
                socket.localRotation = Quaternion.identity;

                var feel = GetOrAdd<CameraFeel>(camera.gameObject);
                var view = root.GetComponent<FirstPersonView>();
                view.feel = feel;
                view.sprintFov = 6;

                var thrower = root.GetComponent<CardThrower>();
                thrower.launchSocket = socket;
                var line = thrower.trajectory;
                if (line)
                {
                    line.sharedMaterial = ReworkVisuals.ParticleMaterial("Aim dots", "fx_dot.png", false);
                    line.textureMode = LineTextureMode.Tile;
                    line.widthMultiplier = .05f;
                    line.alignment = LineAlignment.View;
                    line.numCapVertices = 0;
                    line.useWorldSpace = true;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.sharedMaterial.mainTextureScale = new Vector2(1, 1);
                }

                // Snappier gravity; impulses and fall thresholds are normalised to the design's 9.81.
                var motor = root.GetComponent<FirstPersonMotor>();
                motor.gravity = 19;
                motor.referenceGravity = 9.81f;
                result = hand;
            });
            Say("player rig: card viewmodel, launch socket, camera feel, gravity 19");
            return result;
        }

        // ---------------- creatures ----------------

        static void Creatures()
        {
            Preset("Assets/Game/Prefabs/Timid.prefab", c =>
            {
                c.displayName = "Заяц"; c.maximumHealth = 2; c.attackHalfHearts = 0; c.dash = false;
                c.wanderSpeed = 1.4f; c.speed = 3; c.dashSpeed = 8; c.hopHeight = .14f; c.gaitFrequency = 2.6f;
                c.sightRange = 15; c.hearingRange = 4; c.alertRange = 12; c.fleeRange = 7.5f; c.safeDistance = 15;
                c.dashSeconds = .5f; c.pauseSeconds = .55f; c.leash = 16; c.territoryRadius = 5; c.turnRate = 540;
            }, .5f);
            Preset("Assets/Game/Prefabs/Territorial.prefab", c =>
            {
                c.displayName = "Кабан"; c.maximumHealth = 6; c.attackHalfHearts = 3; c.dash = true;
                c.windup = .9f; c.strikeSeconds = .6f; c.recovery = 1f; c.recoveryOnMiss = 1.6f;
                c.speed = 3.4f; c.wanderSpeed = 1.1f; c.dashSpeed = 9.6f; c.attackRange = 6.5f; c.contactRange = 1.5f;
                c.sightRange = 14; c.threatRange = 10; c.calmRange = 13; c.aggroRange = 6; c.provokeSeconds = 1.1f;
                c.leash = 18; c.territoryRadius = 5; c.hopHeight = .05f; c.gaitFrequency = 2f; c.turnRate = 300;
            }, 2);
            Preset("Assets/Game/Prefabs/Aggressive.prefab", c =>
            {
                c.displayName = "Волк"; c.maximumHealth = 8; c.attackHalfHearts = 2; c.dash = true;
                c.windup = .65f; c.strikeSeconds = .25f; c.recovery = 1f; c.recoveryOnMiss = 1f;
                c.speed = 5.2f; c.wanderSpeed = 1.6f; c.dashSpeed = 8.5f; c.attackRange = 2.6f; c.contactRange = 1.4f;
                c.sightRange = 15; c.hearingRange = 5; c.leash = 26; c.territoryRadius = 6; c.hopHeight = .09f; c.gaitFrequency = 2.4f; c.turnRate = 420;
            }, 1);
        }

        static void Preset(string path, Action<WorldCreature> apply, float mass)
        {
            EditPrefab(path, root =>
            {
                var creature = root.GetComponent<WorldCreature>();
                apply(creature);
                creature.body.mass = mass;
                creature.body.interpolation = RigidbodyInterpolation.Interpolate;
                creature.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var overheadRoot = Find(root.transform, "Overhead");
                if (overheadRoot) Object.DestroyImmediate(overheadRoot.gameObject);
                overheadRoot = Child(root.transform, "Overhead").transform;
                overheadRoot.localPosition = new Vector3(0, 2.05f, 0);
                var overhead = overheadRoot.gameObject.AddComponent<CreatureOverhead>();
                var alert = Child(overheadRoot, "Alert", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                alert.sprite = overhead.alertCalm = ReworkVisuals.UiSprite("ui_alert.png");
                overhead.alertDanger = ReworkVisuals.UiSprite("ui_alert_red.png");
                alert.sortingOrder = 20;
                overhead.alert = alert;
                overhead.pipFull = ReworkVisuals.UiSprite("ui_pip_full.png");
                overhead.pipEmpty = ReworkVisuals.UiSprite("ui_pip_empty.png");
                overhead.pips = Enumerable.Range(0, 8).Select(i =>
                {
                    var pip = Child(overheadRoot, "Pip " + (i + 1), typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                    pip.sprite = overhead.pipFull;
                    pip.sortingOrder = 19;
                    float offset = (i - (creature.maximumHealth - 1) * .5f) * .17f;
                    pip.transform.localPosition = new Vector3(offset, -.62f, 0);
                    return pip;
                }).ToArray();
                creature.overhead = overhead;
            });
            Say("creature preset " + path);
        }

        // ---------------- loot ----------------

        static void Loot(GameObject glint)
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs", "Assets/Expedition/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<WorldLoot>())
                .ToArray();
            foreach (var path in paths)
            {
                bool variant = PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(path)) == PrefabAssetType.Variant;
                bool trophy = path.Contains("Trophy");
                if (variant && !trophy) continue;
                EditPrefab(path, root =>
                {
                    var loot = root.GetComponent<WorldLoot>();
                    if (!variant || !loot.glint)
                    {
                        var existing = Find(root.transform, "Glint");
                        if (!existing) existing = ((GameObject)PrefabUtility.InstantiatePrefab(glint, root.transform)).transform;
                        loot.glint = existing.GetComponentInChildren<ParticleSystem>();
                    }
                    if (trophy)
                    {
                        var body = GetOrAdd<Rigidbody>(root);
                        body.mass = .4f;
                        body.isKinematic = true;
                        body.interpolation = RigidbodyInterpolation.Interpolate;
                        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        body.angularDamping = 2;
                        loot.body = body;
                    }
                });
                Say("loot " + path + (variant ? " (variant)" : ""));
            }
        }

        // ---------------- HUD ----------------

        static ExpeditionHud BuildHud()
        {
            var root = new GameObject("Expedition HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 5;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1;
            var hud = root.AddComponent<ExpeditionHud>();
            hud.canvas = canvas;
            Color paper = new Color(.97f, .93f, .82f);
            var plate = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var vignette = Stretch(root.transform, "Vignette");
            hud.vignette = Image(vignette, ReworkVisuals.UiSprite("ui_vignette.png"), new Color(0, 0, 0, 0));

            var hearts = Rect(root.transform, "Hearts", new Vector2(0, 1), new Vector2(36, -32), new Vector2(300, 60));
            hud.hearts = hearts;
            hud.heartFills = Enumerable.Range(0, 4).Select(i =>
            {
                var slot = Rect(hearts, "Heart " + (i + 1), new Vector2(0, .5f), new Vector2(i * 62 + 32, 0), new Vector2(64, 60), new Vector2(.5f, .5f));
                Image(slot, ReworkVisuals.UiSprite("ui_heart_empty.png"), Color.white);
                var fill = Image(Stretch(slot, "Fill"), ReworkVisuals.UiSprite("ui_heart.png"), new Color(.86f, .24f, .2f));
                fill.type = UnityEngine.UI.Image.Type.Filled;
                fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                fill.fillOrigin = 0;
                return fill;
            }).ToArray();

            hud.loot = Text(Rect(root.transform, "Loot", new Vector2(1, 1), new Vector2(-36, -36), new Vector2(420, 40)), true, 32, TextAlignmentOptions.TopRight, paper);
            hud.toasts = Text(Rect(root.transform, "Toasts", new Vector2(1, 1), new Vector2(-36, -84), new Vector2(520, 200)), false, 18, TextAlignmentOptions.TopRight, paper);
            hud.toasts.lineSpacing = 12;

            var noticePlate = Rect(root.transform, "Notice", new Vector2(.5f, 1), new Vector2(0, -112), new Vector2(900, 40));
            hud.notice = Text(noticePlate, false, 18, TextAlignmentOptions.Center, paper);

            var reticle = Rect(root.transform, "Reticle", new Vector2(.5f, .5f), Vector2.zero, new Vector2(96, 96));
            hud.reticle = Image(Rect(reticle, "Dot", new Vector2(.5f, .5f), Vector2.zero, new Vector2(16, 16)), ReworkVisuals.UiSprite("ui_dot.png"), Color.white);
            var ring = Image(Rect(reticle, "Charge ring", new Vector2(.5f, .5f), Vector2.zero, new Vector2(72, 72)), ReworkVisuals.UiSprite("ui_ring.png"), Color.white);
            ring.type = UnityEngine.UI.Image.Type.Filled;
            ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            ring.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.enabled = false;
            hud.chargeRing = ring;
            hud.spinArrow = Image(Rect(reticle, "Spin", new Vector2(.5f, .5f), new Vector2(0, 58), new Vector2(64, 32)), ReworkVisuals.UiSprite("ui_spin.png"), Color.white);
            hud.spinArrow.enabled = false;

            hud.cardLabel = Text(Rect(root.transform, "Card label", new Vector2(.5f, .5f), new Vector2(0, -74), new Vector2(600, 40)), true, 32, TextAlignmentOptions.Center, paper);
            hud.prompt = Text(Rect(root.transform, "Prompt", new Vector2(.5f, .5f), new Vector2(0, -116), new Vector2(700, 30)), false, 18, TextAlignmentOptions.Center, paper);
            hud.hint = Text(Rect(root.transform, "Hint", new Vector2(.5f, .5f), new Vector2(0, -158), new Vector2(900, 30)), false, 18, TextAlignmentOptions.Center, new Color(1, 1, 1, .75f));
            hud.deck = Text(Rect(root.transform, "Decks", new Vector2(0, 0), new Vector2(36, 30), new Vector2(560, 40)), true, 32, TextAlignmentOptions.BottomLeft, paper);

            var stamina = Rect(root.transform, "Stamina", new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(96, 6));
            Image(stamina, plate, new Color(0, 0, 0, .45f)).type = UnityEngine.UI.Image.Type.Sliced;
            var staminaFill = Image(Stretch(stamina, "Fill"), plate, new Color(1, .93f, .7f, .9f));
            staminaFill.type = UnityEngine.UI.Image.Type.Filled;
            staminaFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            hud.staminaPanel = stamina.gameObject;
            hud.staminaFill = staminaFill;
            stamina.gameObject.SetActive(false);

            var bag = Rect(root.transform, "Bag", new Vector2(1, .5f), new Vector2(-36, 0), new Vector2(460, 320));
            var bagImage = Image(bag, plate, new Color(.08f, .07f, .06f, .82f));
            bagImage.type = UnityEngine.UI.Image.Type.Sliced;
            Text(Rect(bag, "Title", new Vector2(0, 1), new Vector2(24, -18), new Vector2(400, 36)), true, 32, TextAlignmentOptions.TopLeft, paper, "Сумка");
            hud.inventory = Text(Rect(bag, "Contents", new Vector2(0, 1), new Vector2(24, -66), new Vector2(410, 240)), false, 18, TextAlignmentOptions.TopLeft, paper);
            hud.inventory.lineSpacing = 14;
            hud.inventoryPanel = bag.gameObject;
            bag.gameObject.SetActive(false);

            canvas.enabled = false;
            var prefab = SavePrefab(root, UiFolder + "Expedition HUD.prefab");
            Say("HUD prefab built");
            return prefab.GetComponent<ExpeditionHud>();
        }

        static ScreenFade BuildFade()
        {
            var root = new GameObject("Screen fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var fade = root.AddComponent<ScreenFade>();
            fade.canvas = canvas;
            fade.cover = Image(Stretch(root.transform, "Cover"), null, new Color(.035f, .04f, .045f, 0));
            var title = Rect(root.transform, "Title", new Vector2(.5f, .5f), new Vector2(0, 180), new Vector2(1000, 140));
            fade.titleGroup = title.gameObject.AddComponent<CanvasGroup>();
            fade.titleGroup.alpha = 0;
            fade.titleGroup.blocksRaycasts = false;
            fade.title = Text(Rect(title, "Heading", new Vector2(.5f, 1), Vector2.zero, new Vector2(1000, 72)), true, 64, TextAlignmentOptions.Top, new Color(.98f, .94f, .84f));
            fade.subtitle = Text(Rect(title, "Caption", new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(1000, 30)), false, 18, TextAlignmentOptions.Bottom, new Color(.98f, .94f, .84f, .8f));
            canvas.enabled = false;
            var prefab = SavePrefab(root, UiFolder + "Screen fade.prefab");
            Say("fade prefab built");
            return prefab.GetComponent<ScreenFade>();
        }

        // ---------------- scene ----------------

        static void Scene(ExpeditionVfx vfx, CardProjectile projectile, CardDebris debris, ExpeditionHud hudPrefab, ScreenFade fadePrefab, VfxBurst ice, VfxBurst fire)
        {
            // Prefab handles go stale after the asset database reimports: reload everything by path.
            hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "Expedition HUD.prefab").GetComponent<ExpeditionHud>();
            fadePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "Screen fade.prefab").GetComponent<ScreenFade>();
            projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Cards/Thrown card.prefab").GetComponent<CardProjectile>();
            debris = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Cards/Card debris.prefab").GetComponent<CardDebris>();
            ice = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Effects/Ice burst.prefab").GetComponent<VfxBurst>();
            fire = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Effects/Fire burst.prefab").GetComponent<VfxBurst>();
            vfx = AssetDatabase.LoadAssetAtPath<ExpeditionVfx>("Assets/Game/Data/Expedition Effects.asset");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var world = Object.FindAnyObjectByType<ExpeditionWorld>(FindObjectsInactive.Include);
            var roots = scene.GetRootGameObjects();

            // Feel services: one owner of time scale and the pooled voices.
            var feelRoot = roots.FirstOrDefault(r => r.name == "Game feel");
            if (!feelRoot) feelRoot = new GameObject("Game feel");
            var time = GetOrAdd<TimeFeel>(feelRoot);
            var sfx = GetOrAdd<SfxPlayer>(feelRoot);
            DestroyChildren(feelRoot.transform);
            sfx.pool = Enumerable.Range(1, 20).Select(i =>
            {
                var source = Child(feelRoot.transform, "Voice " + i, typeof(AudioSource)).GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.dopplerLevel = 0;
                return source;
            }).ToArray();

            // UI: replace the old expedition HUD with the new prefab, add the fade overlay.
            foreach (var root in scene.GetRootGameObjects())
                if (root && (root.GetComponentInChildren<ExpeditionHud>(true) || root.GetComponentInChildren<ScreenFade>(true)))
                { Say("removing " + root.name); Object.DestroyImmediate(root); }
            var hudAsset = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "Expedition HUD.prefab");
            var fadeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(UiFolder + "Screen fade.prefab");
            Say("hud asset " + (hudAsset ? "ok" : "MISSING") + ", fade asset " + (fadeAsset ? "ok" : "MISSING"));
            var hud = ((GameObject)PrefabUtility.InstantiatePrefab(hudAsset, scene)).GetComponent<ExpeditionHud>();
            var fade = ((GameObject)PrefabUtility.InstantiatePrefab(fadeAsset, scene)).GetComponent<ScreenFade>();
            hud.world = world;

            var player = world.motor;
            var hand = player.GetComponentInChildren<CardHandView>(true);
            hand.world = world;
            world.hud = hud;
            world.hand = hand;
            world.feel = player.GetComponentInChildren<CameraFeel>(true);
            world.time = time;
            world.sfx = sfx;
            world.fade = fade;
            world.vfx = vfx;
            world.projectilePrefab = projectile;
            world.debrisPrefab = debris;
            world.cues = Asset<ExpeditionAudio>("Assets/Game/Data/Expedition Audio.asset");
            var legacySource = world.GetComponent<AudioSource>();
            if (legacySource) Object.DestroyImmediate(legacySource);

            // Obstacles: the lake gets a solid water surface for cards and a walkable ice slab instead of a 9.5 m sphere.
            foreach (var obstacle in world.obstacles)
            {
                obstacle.world = world;
                if (obstacle.kind == ObstacleKind.Lake)
                {
                    var surface = Find(obstacle.transform, "Water surface");
                    if (!surface) surface = Child(obstacle.transform, "Water surface", typeof(BoxCollider)).transform;
                    surface.gameObject.layer = 4;
                    surface.position = new Vector3(obstacle.transform.position.x, -.36f, obstacle.transform.position.z);
                    var box = surface.GetComponent<BoxCollider>();
                    box.size = new Vector3(19.5f, .1f, 19.5f);
                    obstacle.waterSurface = box;
                    foreach (var capsule in obstacle.changed.GetComponentsInChildren<CapsuleCollider>(true))
                    {
                        var go = capsule.gameObject;
                        Object.DestroyImmediate(capsule);
                        var slab = GetOrAdd<BoxCollider>(go);
                        slab.size = new Vector3(.95f, 2, .95f);
                    }
                    obstacle.reactVfx = ice;
                    obstacle.tint = new Color(.6f, .9f, 1f);
                    obstacle.transformSeconds = 1.1f;
                }
                else
                {
                    obstacle.reactVfx = fire;
                    obstacle.tint = new Color(1f, .55f, .2f);
                    obstacle.transformSeconds = 1.2f;
                }
                EditorUtility.SetDirty(obstacle);
            }

            // Creatures: scene instances may carry old overrides — re-apply the presets on top.
            foreach (var creature in world.creatures)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(creature);
                if (source)
                {
                    var so = new SerializedObject(creature);
                    foreach (var name in new[] { "speed", "attackRange", "contactRange", "windup", "strikeSeconds", "recovery", "recoveryOnMiss", "dash", "maximumHealth", "attackHalfHearts", "sightRange", "leash", "displayName" })
                    {
                        var property = so.FindProperty(name);
                        if (property != null && property.prefabOverride) PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                    }
                }
                creature.world = world;
            }
            foreach (var loot in world.finds) loot.world = world;

            // Changes on prefab instances must be recorded as overrides or they are lost on save.
            foreach (var component in new Component[] { hud, hand, world }.Concat(world.creatures).Concat(world.finds).Concat(world.obstacles))
                if (component && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);

            // Build settings: the game is one scene.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            EditorUtility.SetDirty(world);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Say("scene wired: feel services, HUD, fade, lake surface/ice slab, creatures");
        }
    }
}
