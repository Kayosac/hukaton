using System;
using System.Linq;
using Hakaton.Character;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.Game;
using Hakaton.Trading.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Hakaton.Authoring.Kit;
using Object = UnityEngine.Object;

namespace Hakaton.Authoring
{
    /// <summary>Shop feel pass. Invoke: Hakaton.Authoring.ReworkShop.Run</summary>
    static class ReworkShop
    {
        const string Cues = "Assets/Audio/Cues/";
        static SfxCue C(string name) => Load<SfxCue>(Cues + name + ".asset");

        static SfxCue Derive(string name, string from, float pitchMin, float pitchMax, float volume)
        {
            var cue = Asset<SfxCue>(Cues + name + ".asset");
            cue.clips = C(from).clips;
            cue.pitch = new Vector2(pitchMin, pitchMax);
            cue.volume = volume;
            cue.spatialBlend = 0;
            cue.cooldown = .02f;
            EditorUtility.SetDirty(cue);
            return cue;
        }

        public static string Run()
        {
            Log.Clear();
            try
            {
                var audio = Asset<ShopAudio>("Assets/Game/Data/Shop Audio.asset");
                audio.doorBell = C("door_bell"); audio.npcNeutral = C("npc_hmm"); audio.npcHappy = C("npc_happy");
                audio.npcAngry = C("npc_angry"); audio.walkOut = C("deal_fail");
                audio.saleSmall = C("coins_small"); audio.saleBig = C("coins_big");
                audio.coinTick = Derive("coin_tick", "coin_single", 1.5f, 1.7f, .3f);
                audio.placeItem = C("place_item"); audio.takeItem = Derive("take_item", "place_item", 1.25f, 1.35f, .5f);
                audio.slotHover = C("slot_hover"); audio.clockTick = C("clock_tick");
                audio.shopOpen = C("shop_open"); audio.shopClose = C("shop_close"); audio.priceTap = C("ui_click");
                audio.ambience = C("shop_loop");
                foreach (var cue in new[] { audio.doorBell, audio.npcNeutral, audio.npcHappy, audio.npcAngry, audio.walkOut, audio.saleSmall, audio.saleBig, audio.shopOpen, audio.shopClose })
                { cue.spatialBlend = 0; EditorUtility.SetDirty(cue); }
                EditorUtility.SetDirty(audio);

                var coinMaterial = ReworkVisuals.ParticleMaterial("Coin", "fx_coin.png", false);
                AssetDatabase.SaveAssets();

                var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Hakaton.unity", OpenSceneMode.Single);
                var trade = Object.FindAnyObjectByType<TradingPrototype>(FindObjectsInactive.Include);
                var game = Object.FindAnyObjectByType<GameLoop>(FindObjectsInactive.Include);
                var world = Object.FindAnyObjectByType<ExpeditionWorld>(FindObjectsInactive.Include);
                var sfx = Object.FindAnyObjectByType<SfxPlayer>(FindObjectsInactive.Include);
                var fade = Object.FindAnyObjectByType<ScreenFade>(FindObjectsInactive.Include);
                var feel = world.motor.GetComponentInChildren<CameraFeel>(true);

                // Coin burst at the counter: authored once, emitted on each sale.
                var anchor = trade.World.DealItemAnchor ? trade.World.DealItemAnchor : trade.transform;
                var burstRoot = Find(anchor.parent ? anchor.parent : anchor, "Coin burst");
                if (burstRoot) Object.DestroyImmediate(burstRoot.gameObject);
                var burstGo = new GameObject("Coin burst", typeof(ParticleSystem));
                burstGo.transform.SetParent(anchor.parent ? anchor.parent : anchor, false);
                burstGo.transform.position = anchor.position + Vector3.up * .05f;
                var ps = burstGo.GetComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.playOnAwake = false; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.1f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(.06f, .1f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.gravityModifier = 1.4f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;
                var emission = ps.emission; emission.rateOverTime = 0;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 30; shape.radius = .05f;
                shape.rotation = new Vector3(-90, 0, 0);
                var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-8, 8);
                var collision = ps.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World;
                collision.bounce = .45f; collision.dampen = .3f; collision.lifetimeLoss = .15f;
                var colour = ps.colorOverLifetime; colour.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .75f), new GradientAlphaKey(0, 1) });
                colour.color = gradient;
                var renderer = burstGo.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = coinMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var feelHost = trade.gameObject;
                var shopFeel = GetOrAdd<ShopFeel>(feelHost);
                shopFeel.trade = trade; shopFeel.hud = trade.hud; shopFeel.sfx = sfx; shopFeel.cues = audio;
                shopFeel.feel = feel; shopFeel.coinBurst = ps;
                trade.hud.feelDrivesWallet = true;
                EditorUtility.SetDirty(trade.hud);
                if (PrefabUtility.IsPartOfPrefabInstance(trade.hud)) PrefabUtility.RecordPrefabInstancePropertyModifications(trade.hud);

                // A second visitor so the next one walks in while the previous one leaves.
                var presenter = Object.FindAnyObjectByType<TradingCustomerPresenter>(FindObjectsInactive.Include);
                var first = presenter.actor;
                SpriteNpcActor second = presenter.extraActors != null && presenter.extraActors.Length > 0 ? presenter.extraActors[0] : null;
                if (!second)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(first.gameObject);
                    var copy = source ? (GameObject)PrefabUtility.InstantiatePrefab(source, first.transform.parent) : Object.Instantiate(first.gameObject, first.transform.parent);
                    copy.name = first.gameObject.name + " B";
                    copy.transform.SetPositionAndRotation(first.transform.position, first.transform.rotation);
                    second = copy.GetComponent<SpriteNpcActor>();
                    EditorUtility.CopySerialized(first, second);
                    if (PrefabUtility.IsPartOfPrefabInstance(second)) PrefabUtility.RecordPrefabInstancePropertyModifications(second);
                    presenter.extraActors = new[] { second };
                }
                // Copied references must point at the copy's own rig, not the original's children.
                T Remap<T>(T original) where T : Component
                {
                    if (!original || !original.transform.IsChildOf(first.transform)) return original;
                    string path = AnimationUtility.CalculateTransformPath(original.transform, first.transform);
                    var match = string.IsNullOrEmpty(path) ? second.transform : second.transform.Find(path);
                    return match ? match.GetComponent<T>() : null;
                }
                second.spriteRenderer = Remap(first.spriteRenderer);
                second.facingRoot = Remap(first.facingRoot);
                second.stepRoot = Remap(first.stepRoot);
                Say($"second visitor rig: sprite={second.spriteRenderer?.name} facing={second.facingRoot?.name} step={second.stepRoot?.name}");
                foreach (var actor in new[] { first, second })
                {
                    actor.reactionDuration = .7f; actor.turnDuration = .3f; actor.walkSpeed = 2.8f;
                    EditorUtility.SetDirty(actor);
                    if (PrefabUtility.IsPartOfPrefabInstance(actor)) PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                }
                EditorUtility.SetDirty(presenter);

                game.fade = fade; game.sfx = sfx;
                game.departCue = C("transition_whoosh"); game.titleCue = C("title_sting"); game.goalCue = C("goal_fanfare");
                EditorUtility.SetDirty(game);
                EditorUtility.SetDirty(shopFeel);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Say("shop wired: audio, coin burst, second visitor, departure transition");
            }
            catch (Exception exception) { Say("FAILED " + exception); }
            return Log.ToString();
        }
    }
}
