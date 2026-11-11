using System.IO;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.TempAuthoring
{
    /// <summary>TEMPORARY one-off authoring of the oddity assets and lab. Removed after the assets are saved.</summary>
    static class OddityAuthoring
    {
        const string Art = "Assets/OddityArt";
        const string Prefabs = "Assets/Expedition/Prefabs/Oddities";
        const string Data = "Assets/Game/Data/Oddities";
        const string Fx = "Assets/Game/Prefabs/Effects/Retro/Oddities";
        const string Cues = "Assets/Audio/Cues";
        const string Lab = "Assets/OddityArt/Scenes/OddityLab.unity";

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Texture2D Pixel(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Lit(string name, Texture2D texture, bool emissive = false)
        {
            string path = Art + "/Materials/" + name + ".mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/ChestArt/Materials/M_Chest_CH01.mat");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(source); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(source);
            m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Crt(Texture2D picture)
        {
            string path = Art + "/Materials/M_OD02_Screen.mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Computer/Art/M_CrtScreen.mat");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(source); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(source);
            m.SetTexture("_MainTex", picture);
            m.SetFloat("_Noise", .85f);
            m.SetFloat("_Curve", .12f);
            m.SetFloat("_Brightness", 1.4f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static SfxCue Cue(string name, float volume, float blend, float maxDistance, Vector2 pitch, params string[] clips)
        {
            string path = Cues + "/" + name + ".asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            if (!cue) { cue = ScriptableObject.CreateInstance<SfxCue>(); AssetDatabase.CreateAsset(cue, path); }
            cue.clips = clips.Select(c => AssetDatabase.LoadAssetAtPath<AudioClip>(c) ?? throw new System.Exception("clip " + c)).ToArray();
            cue.volume = volume; cue.spatialBlend = blend; cue.minDistance = 2; cue.maxDistance = maxDistance; cue.pitch = pitch;
            EditorUtility.SetDirty(cue);
            return cue;
        }

        static SfxCue Existing(string name) => AssetDatabase.LoadAssetAtPath<SfxCue>(Cues + "/" + name + ".asset") ?? throw new System.Exception("cue " + name);

        static VfxBurst Wrap(string raPath, string name, float lifetime)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(raPath));
            instance.name = name;
            var burst = instance.GetComponent<VfxBurst>();
            if (!burst) burst = instance.AddComponent<VfxBurst>();
            var root = instance.GetComponent<ParticleSystem>();
            var all = instance.GetComponentsInChildren<ParticleSystem>(true);
            burst.systems = root ? new[] { root } : all.Where(s => s.transform.parent == instance.transform).ToArray();
            burst.tinted = new ParticleSystem[0];
            burst.lifetime = lifetime;
            foreach (var s in all) { var main = s.main; main.playOnAwake = false; }
            foreach (var a in instance.GetComponentsInChildren<AudioSource>(true)) { a.playOnAwake = false; a.enabled = false; }
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, Fx + "/" + name + ".prefab");
            Object.DestroyImmediate(instance);
            return saved.GetComponent<VfxBurst>();
        }

        static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = Data + "/" + name + ".asset";
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!a) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); }
            return a;
        }

        static Light AddLight(Transform parent, Vector3 at, Color colour, float range)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = 2;
            light.shadows = LightShadows.None;
            return light;
        }

        public static string Run()
        {
            Folder(Art + "/Materials"); Folder(Art + "/Scenes"); Folder(Prefabs); Folder(Data); Folder(Fx);
            var phoneTex = Pixel(Art + "/Textures/T_Payphone.png");
            var tvTex = Pixel(Art + "/Textures/T_OldTV.png");
            var eye = Pixel(Art + "/Textures/T_OldTV_Eye.png");
            var snow = Pixel(Art + "/Textures/T_OldTV_Static.png");
            foreach (var model in new[] { Art + "/Models/SM_OD01_Payphone.fbx", Art + "/Models/SM_OD02_OldTV.fbx" })
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(model);
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.SaveAndReimport();
            }
            var mPhone = Lit("M_OD01_Payphone", phoneTex);
            var mLamp = Lit("M_OD01_Lamp", phoneTex, true);
            var mTv = Lit("M_OD02_OldTV", tvTex);
            var mScreen = Crt(snow);

            var ring = Cue("oddity_phone_ring", .9f, 1, 28, new Vector2(.97f, 1.03f), "Assets/Audio/Sfx/oddities/oddity_phone_ring_loop.wav");
            var hiss = Cue("oddity_tv_hiss", .8f, 1, 25, new Vector2(.97f, 1.03f), "Assets/Audio/Sfx/oddities/oddity_tv_hiss_loop.wav");
            var voice = Cue("oddity_phone_voice", .95f, .5f, 20, new Vector2(.9f, 1f), "Assets/Audio/Sfx/oddities/oddity_phone_voice.wav");
            var knock = Cue("oddity_tv_knock", .95f, .6f, 25, new Vector2(.95f, 1.03f), "Assets/Audio/Sfx/oddities/oddity_tv_knock.wav");
            var call = Cue("oddity_call", .9f, 0, 40, new Vector2(.95f, 1f), "Assets/Audio/Sfx/oddities/oddity_call.wav");

            var question = Wrap("Assets/Plugins/Retro Arsenal/Prefabs/Interactive/Status Icons/Question.prefab", "Oddity question", 2);
            var glitch = Wrap("Assets/Plugins/Retro Arsenal/Prefabs/Combat/Explosions/Glitter/GlitterExplosionPurple.prefab", "Oddity glitch", 2.5f);
            var healPoof = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Effects/Retro/Heal poof.prefab").GetComponent<VfxBurst>();

            // ---- effects and their table ----
            var heal = Asset<HealOddityEffect>("Heal");
            heal.halfHearts = 3; heal.notice = "Что-то тёплое на том конце. Здоровье восстановлено";
            heal.cue = Existing("heal_chime"); heal.burst = healPoof; heal.burstScale = .8f; heal.tint = new Color(.6f, 1f, .6f);
            var moon = Asset<MoonGravityOddityEffect>("Moon gravity");
            moon.card = AssetDatabase.LoadAssetAtPath<MoonGravityEffect>("Assets/Game/Data/Effects/Moon gravity.asset");
            moon.seconds = 10; moon.notice = "Тело стало лёгким, как на Луне";
            moon.cue = Existing("transition_whoosh"); moon.burst = null;
            var lure = Asset<LureOddityEffect>("Lure creatures");
            lure.radius = 25; lure.seconds = 15; lure.notice = "Лес услышал. Звери идут сюда";
            lure.cue = call; lure.burst = null;
            var table = Asset<OddityTable>("Oddity effects");
            table.entries = new[] {
                new OddityTable.Entry { effect = heal, weight = 4 },
                new OddityTable.Entry { effect = moon, weight = 3 },
                new OddityTable.Entry { effect = lure, weight = 3 } };
            foreach (var a in new ScriptableObject[] { heal, moon, lure, table }) EditorUtility.SetDirty(a);

            // ---- payphone prefab ----
            var phoneModel = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/SM_OD01_Payphone.fbx");
            var phone = new GameObject("Oddity Payphone");
            var pm = (GameObject)PrefabUtility.InstantiatePrefab(phoneModel, phone.transform);
            pm.name = "Model";
            pm.transform.Find("OD01_Body").GetComponent<Renderer>().sharedMaterial = mPhone;
            pm.transform.Find("OD01_Handset").GetComponent<Renderer>().sharedMaterial = mPhone;
            var lampRenderer = pm.transform.Find("OD01_Lamp").GetComponent<Renderer>();
            lampRenderer.sharedMaterial = mLamp;
            var pc = phone.AddComponent<BoxCollider>();
            pc.center = new Vector3(0, 1.0f, .03f); pc.size = new Vector3(.58f, 2f, .5f);
            var po = phone.AddComponent<Oddity>();
            po.kind = Oddity.Kind.Payphone; po.effects = table; po.prompt = "E — снять трубку";
            po.callLoop = ring; po.callVolume = 1;
            po.glow = AddLight(phone.transform, new Vector3(0, 1.58f, .22f), new Color(.82f, 1f, .78f), 7);
            po.glowIntensity = 2.2f; po.flicker = .5f;
            po.lamp = lampRenderer;
            po.answerCue = voice; po.answerSeconds = 1.6f; po.answerBurst = question; po.answerBurstScale = .35f;
            po.handset = pm.transform.Find("OD01_Handset");
            po.handsetDangle = new Vector3(0, 0, -150);
            PrefabUtility.SaveAsPrefabAsset(phone, Prefabs + "/Oddity Payphone.prefab");
            Object.DestroyImmediate(phone);

            // ---- television prefab ----
            var tvModel = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/SM_OD02_OldTV.fbx");
            var tv = new GameObject("Oddity Television");
            var tm = (GameObject)PrefabUtility.InstantiatePrefab(tvModel, tv.transform);
            tm.name = "Model";
            tm.transform.Find("OD02_Body").GetComponent<Renderer>().sharedMaterial = mTv;
            var screenRenderer = tm.transform.Find("OD02_Screen").GetComponent<Renderer>();
            screenRenderer.sharedMaterial = mScreen;
            var tc = tv.AddComponent<BoxCollider>();
            tc.center = new Vector3(0, .5f, -.06f); tc.size = new Vector3(.8f, 1f, .64f);
            var to = tv.AddComponent<Oddity>();
            to.kind = Oddity.Kind.Television; to.effects = table; to.prompt = "E — стукнуть по телевизору";
            to.callLoop = hiss; to.callVolume = 1;
            to.glow = AddLight(tv.transform, new Vector3(-.07f, .66f, .55f), new Color(.7f, .82f, 1f), 6);
            to.glowIntensity = 2.4f; to.flicker = .55f;
            to.answerCue = knock; to.answerSeconds = 1.3f; to.answerBurst = glitch; to.answerBurstScale = .35f; to.answerTint = Color.white;
            to.screen = screenRenderer; to.staticPicture = snow; to.answerPicture = eye; to.screenAspect = .44f / .36f;
            PrefabUtility.SaveAsPrefabAsset(tv, Prefabs + "/Oddity Television.prefab");
            Object.DestroyImmediate(tv);

            AssetDatabase.SaveAssets();
            return "oddities authored";
        }

        public static string BuildLab()
        {
            if (EditorApplication.isPlaying) return "FAILED playing";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "FAILED dirty " + SceneManager.GetSceneAt(i).path;
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Lab)) AssetDatabase.CopyAsset("Assets/EnemyDebug/Scenes/EnemyDebug.unity", Lab);
            var scene = EditorSceneManager.OpenScene(Lab);
            var world = Object.FindObjectsByType<ExpeditionWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            var old = GameObject.Find("Oddity lab");
            if (old) Object.DestroyImmediate(old);
            var group = new GameObject("Oddity lab").transform;
            GameObject Place(string path, Vector3 at, float yaw)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), group);
                go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
                return go;
            }
            // In front of the player's start (it looks along +z from z = -11): the fronts face it.
            Place(Prefabs + "/Oddity Payphone.prefab", new Vector3(-2.2f, 0, -5), 180).GetComponent<Oddity>().world = world;
            Place(Prefabs + "/Oddity Television.prefab", new Vector3(2.2f, 0, -5), 180).GetComponent<Oddity>().world = world;
            Place("Assets/Expedition/Prefabs/Chests/Chest 2 Common.prefab", new Vector3(6, 0, -5), 0).GetComponent<TreasureChest>().world = world;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "lab built";
        }
    }
}
