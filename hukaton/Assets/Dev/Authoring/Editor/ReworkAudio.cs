using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Character;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Hakaton.Authoring.Kit;
using Object = UnityEngine.Object;

namespace Hakaton.Authoring
{
    /// <summary>Turns Assets/Audio/Sfx/manifest.json into SfxCue assets and wires them. Invoke: Hakaton.Authoring.ReworkAudio.Run</summary>
    static class ReworkAudio
    {
        const string Root = "Assets/Audio/Sfx/";
        const string Cues = "Assets/Audio/Cues/";

        [Serializable] class Entry { public string cue, file, category; public string[] variants; public bool loop; public float suggestedVolume; public float[] pitchRange; }
        [Serializable] class Manifest { public Entry[] items; }

        static readonly Dictionary<string, SfxCue> Made = new Dictionary<string, SfxCue>();

        public static string Run()
        {
            Log.Clear();
            Made.Clear();
            try
            {
                string json = File.ReadAllText(Root + "manifest.json");
                var manifest = JsonUtility.FromJson<Manifest>("{\"items\":" + json + "}");
                foreach (var entry in manifest.items) Make(entry);
                // Derived cues: same recordings, different character.
                Derive("charge_ready", "card_select", 1.45f, 1.55f, .9f, 0);
                Derive("card_cancel", "card_select", .7f, .78f, .6f, 0);
                Derive("hurt_small", "creature_hurt", 1.25f, 1.4f, .8f, .85f);
                Derive("hurt_big", "creature_hurt", .72f, .82f, 1, .85f);
                Derive("step_small", "hare_hop", 1.05f, 1.2f, .45f, .85f);
                Derive("step_heavy", "hare_hop", .55f, .65f, .7f, .85f);
                Derive("step_wolf", "hare_hop", .8f, .9f, .5f, .85f);
                Derive("return_ready", "ui_upgrade", 1.1f, 1.1f, .6f, 0);
                AssetDatabase.SaveAssets();
                Wire();
                Say("DONE " + Made.Count + " cues");
            }
            catch (Exception exception) { Say("FAILED " + exception); }
            return Log.ToString();
        }

        static float SpatialFor(string category) => category switch
        {
            "impacts" or "creatures" => .85f,
            "shop" or "backroom" => .5f,
            _ => 0
        };

        static void Make(Entry entry)
        {
            var clips = entry.variants.Select(v => AssetDatabase.LoadAssetAtPath<AudioClip>(Root + v)).Where(c => c).ToArray();
            foreach (var v in entry.variants)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(Root + v);
                if (!importer) continue;
                importer.forceToMono = true;
                var settings = importer.defaultSampleSettings;
                bool longSound = entry.category == "ambience";
                settings.loadType = longSound ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = longSound ? .6f : .85f;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            var cue = Asset<SfxCue>(Cues + entry.cue + ".asset");
            cue.clips = clips;
            cue.volume = entry.suggestedVolume > 0 ? entry.suggestedVolume : .8f;
            cue.pitch = entry.pitchRange != null && entry.pitchRange.Length == 2 ? new Vector2(entry.pitchRange[0], entry.pitchRange[1]) : new Vector2(.95f, 1.05f);
            cue.spatialBlend = SpatialFor(entry.category);
            cue.cooldown = entry.loop ? 0 : .03f;
            cue.minDistance = 2;
            cue.maxDistance = 45;
            EditorUtility.SetDirty(cue);
            Made[entry.cue] = cue;
        }

        static void Derive(string name, string from, float pitchMin, float pitchMax, float volume, float spatial)
        {
            var source = Made[from];
            var cue = Asset<SfxCue>(Cues + name + ".asset");
            cue.clips = source.clips;
            cue.pitch = new Vector2(pitchMin, pitchMax);
            cue.volume = volume;
            cue.spatialBlend = spatial;
            cue.cooldown = .03f;
            EditorUtility.SetDirty(cue);
            Made[name] = cue;
        }

        static SfxCue C(string name) => Made.TryGetValue(name, out var cue) ? cue : throw new Exception("No cue " + name);
        static AudioClip[] Clips(string name) => C(name).clips;

        static void Wire()
        {
            var audio = Asset<ExpeditionAudio>("Assets/Game/Data/Expedition Audio.asset");
            audio.cardDraw = C("card_draw"); audio.cardSelect = C("card_select"); audio.deckSwitch = C("deck_switch");
            audio.discard = C("hand_discard"); audio.charge = C("card_charge"); audio.chargeReady = C("charge_ready");
            audio.cancel = C("card_cancel"); audio.cardThrow = C("card_throw"); audio.cardFlutter = C("card_flutter_loop");
            audio.returnAppears = C("return_ready"); audio.returnCard = C("card_return");
            audio.playerHurt = C("player_hurt"); audio.playerHeal = C("heal_chime"); audio.launch = C("launch_whoosh");
            audio.landHeavy = C("land_heavy"); audio.heartbeat = C("heartbeat_low");
            audio.pickup = C("pickup"); audio.trophyDrop = C("trophy_drop");
            audio.ambience = C("forest_loop"); audio.locationTitle = C("title_sting");
            EditorUtility.SetDirty(audio);

            void Effect(string path, string hit, string miss)
            {
                var effect = Load<CardEffect>("Assets/Game/Data/Effects/" + path + ".asset");
                effect.impactSound = C(hit);
                effect.missSound = miss == null ? null : C(miss);
                EditorUtility.SetDirty(effect);
            }
            Effect("Impulse", "impulse_blast", null);
            Effect("Ice", "ice_freeze", null);
            Effect("Fire", "fire_burst", null);
            Effect("Strike", "strike_hit", "impact_paper");
            Effect("Heavy strike", "heavy_hit", "impact_paper");
            Effect("Heal", "heal_chime", null);

            void Creature(string path, string alert, string threat, string windup, string strike, string hurt, string step)
            {
                EditPrefab(path, root =>
                {
                    var creature = root.GetComponent<WorldCreature>();
                    creature.alertCue = alert == null ? null : C(alert);
                    creature.threatCue = threat == null ? null : C(threat);
                    creature.windupCue = windup == null ? null : C(windup);
                    creature.strikeCue = strike == null ? null : C(strike);
                    creature.hurtCue = C(hurt);
                    creature.deathCue = C("creature_death");
                    creature.stepCue = C(step);
                });
            }
            Creature("Assets/Game/Prefabs/Timid.prefab", "hare_alert", null, null, null, "hurt_small", "step_small");
            Creature("Assets/Game/Prefabs/Territorial.prefab", "boar_snort", "boar_snort", "boar_charge", "body_thud", "hurt_big", "step_heavy");
            Creature("Assets/Game/Prefabs/Aggressive.prefab", "wolf_growl", "wolf_growl", "wolf_growl", "wolf_snap", "creature_hurt", "step_wolf");

            EditPrefab("Assets/Game/Prefabs/Player.prefab", root =>
            {
                var steps = root.GetComponent<FirstPersonAudio>();
                if (!steps) return;
                steps.footstepClips = Clips("footstep_grass");
                steps.woodClips = Clips("footstep_wood");
                steps.footstepVolume = .3f;
            });

            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Hakaton.unity", OpenSceneMode.Single);
            var world = Object.FindAnyObjectByType<ExpeditionWorld>(FindObjectsInactive.Include);
            world.cues = audio;
            foreach (var obstacle in world.obstacles)
            {
                obstacle.reactSound = C(obstacle.kind == ObstacleKind.Lake ? "ice_spread" : "fire_burst");
                obstacle.loopSound = obstacle.kind == ObstacleKind.Thorns ? C("fire_crackle_loop") : null;
                EditorUtility.SetDirty(obstacle);
            }
            EditorUtility.SetDirty(world);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Say("audio wired");
        }
    }
}
