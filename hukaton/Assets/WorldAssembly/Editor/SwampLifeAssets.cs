using System.IO;
using System.Linq;
using Hakaton.Feel;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>
    /// The swamp's life and air as assets of the world's content (Kayospace «Биом: Гнилая топь»): the ground fog's
    /// material, a splash effect, the fish, frog and wisp sounds, dragonflies and midges, the will-o'-the-wisp, bubbles
    /// over a sunken chest, and the swamp's colour grading. Made once if missing (edit them freely), wired every build.
    /// </summary>
    public static class SwampLifeAssets
    {
        const string Folder = "Assets/WorldAssembly/Prefabs/Swamp life";
        const string Audio = "Assets/WorldAssembly/Audio/Swamp";
        const string Retro = "Assets/Plugins/Retro Arsenal/Prefabs/Environment/";
        const string CueFolder = "Assets/WorldAssembly/Audio";

        public static void Wire(WorldContent content)
        {
            Directory.CreateDirectory(Folder);
            content.groundFog = FogMaterial();
            content.fishSplash = Splash();
            content.fishCue = Cue("swamp_fish", .55f, 2, 26, "fish_splash_1", "fish_splash_2");
            content.plopCue = Cue("swamp_plop", .45f, 1, 12, "frog_plop");
            content.frogCue = Cue("swamp_frog", .4f, 3, 30, "frog_croak_1", "frog_croak_2", "frog_croak_3", "frog_croak_4");
            content.dragonflies = Dragonflies();
            content.midges = AssetDatabase.LoadAssetAtPath<GameObject>(Retro + "Insects/Flies.prefab");
            content.wisp = Wisp();
            content.sunkenBubbles = AssetDatabase.LoadAssetAtPath<GameObject>(Retro + "Bubbles/Stream/WaterBubbleStream.prefab");
            content.swampLook = Look();
        }

        static Material FogMaterial()
        {
            string path = Folder + "/Ground fog.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("Hakaton/Ground Fog")) { name = "Ground fog" };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static SfxCue Cue(string id, float volume, float min, float max, params string[] clips)
        {
            string path = $"{CueFolder}/{id}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            if (cue) return cue;
            Directory.CreateDirectory(CueFolder);
            cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = clips.Select(c => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Audio}/{c}.wav")).Where(c => c).ToArray();
            cue.volume = volume;
            cue.pitch = new Vector2(.92f, 1.08f);
            cue.spatialBlend = 1;
            cue.minDistance = min;
            cue.maxDistance = max;
            cue.cooldown = .05f;
            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        /// <summary>The ripple splash as a one-shot effect.</summary>
        static VfxBurst Splash()
        {
            string path = Folder + "/Water splash.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<VfxBurst>(path);
            if (existing) return existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Retro + "Liquids/Water/RippleSplash.prefab");
            var root = new GameObject("Water splash");
            if (source)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(source);
                fx.transform.SetParent(root.transform, false);
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main;
                    main.loop = false;
                    main.playOnAwake = false;
                }
            }
            var burst = root.AddComponent<VfxBurst>();
            burst.systems = root.GetComponentsInChildren<ParticleSystem>();
            burst.lifetime = 2.5f;
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<VfxBurst>();
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>Dragonflies: the fireflies turned teal, stretched along their flight, darting.</summary>
        static GameObject Dragonflies()
        {
            string path = Folder + "/Dragonflies.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing) return existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Retro + "Insects/Fireflies/FirefliesBlue.prefab");
            if (!source) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Dragonflies";
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3, 6);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(.07f, .11f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.25f, .75f, .8f), new Color(.45f, .9f, .55f));
                main.maxParticles = 14;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission;
                emission.rateOverTime = 3;
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 9;
                shape.scale = new Vector3(1, .25f, 1);
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 2.4f;
                noise.frequency = 1.3f;
                noise.scrollSpeed = 1.5f;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = .22f;
                renderer.lengthScale = 2.5f;
            }
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved;
        }

        /// <summary>The will-o'-the-wisp: a pale green-blue light with a sparkle at its heart.</summary>
        static WillOWisp Wisp()
        {
            string path = Folder + "/Will-o-the-wisp.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<WillOWisp>(path);
            if (existing) return existing;
            var root = new GameObject("Will-o-the-wisp");
            var wisp = root.AddComponent<WillOWisp>();
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = new Color(.55f, .95f, .85f);
            light.range = 6;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
            wisp.glow = light;
            foreach (var file in new[] { "Sparkle/Single/SparkleSingleGreen.prefab", "Insects/Fireflies/FirefliesGreen.prefab" })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Retro + file);
                if (!source) continue;
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(source);
                fx.transform.SetParent(root.transform, false);
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    if (file.Contains("Fireflies"))
                    {
                        main.maxParticles = 6;
                        var shape = ps.shape;
                        shape.radius = .35f;
                    }
                }
            }
            wisp.drift = Cue("swamp_wisp_drift", .35f, 2, 16, "wisp_drift");
            wisp.vanish = Cue("swamp_wisp_vanish", .5f, 2, 18, "wisp_vanish");
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<WillOWisp>();
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>The swamp's own colour: sickly, a little drained, green in the shadows, darker at the edges.</summary>
        static VolumeProfile Look()
        {
            string path = Folder + "/Swamp look.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var colour = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            colour.saturation.Override(-22);
            colour.colorFilter.Override(new Color(.93f, 1f, .84f));
            colour.contrast.Override(8);
            var tones = profile.Add<UnityEngine.Rendering.Universal.ShadowsMidtonesHighlights>(true);
            tones.shadows.Override(new Vector4(.92f, 1.04f, .9f, -.04f));
            tones.highlights.Override(new Vector4(1.02f, 1.02f, .92f, 0));
            var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vignette.intensity.Override(.3f);
            vignette.smoothness.Override(.45f);
            vignette.color.Override(new Color(.06f, .09f, .05f));
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }
    }
}
