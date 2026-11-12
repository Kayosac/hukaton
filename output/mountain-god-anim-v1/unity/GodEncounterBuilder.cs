// TEMPORARY authoring tool: brings the animated god (MG01_Animated.fbx with its seven clips), the rocks under his
// support hands and the meeting into the god-mountains scene. Run once through the editor bridge
// (invoke Hakaton.GodMountains.EditorTools.GodEncounterBuilder.Build); then delete it with its .meta.
// A copy lives in output/mountain-god-anim-v1/unity/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Hakaton.GodMountains.EditorTools
{
    public static class GodEncounterBuilder
    {
        const string ScenePath = "Assets/GodMountains/Scenes/GodMountains.unity";
        const string GodDir = "Assets/GodMountains/God/MG01";
        const string Fbx = GodDir + "/MG01_Animated.fbx";
        const string RockDir = GodDir + "/Rocks";
        const string ControllerPath = GodDir + "/MG01.controller";
        const string MatDir = "Assets/GodMountains/Art/Materials";
        const string LevelName = "God mountains — level";
        const string BubblePrefab = "Assets/Tutorial/Prefabs/TutorialBubble.prefab";
        const string RocksName = "Скалы под руками-опорами";
        const string EncounterName = "Встреча с богом";
        const string BubbleName = "Плашка реплик бога";
        static readonly string[] Clips = { "Idle", "Notice", "AlertIdle", "Reach", "ReachHold", "Lift", "Talk" };
        static readonly string[] Loops = { "Idle", "AlertIdle", "ReachHold", "Talk" };

        // Bone lengths in god units (output/mountain-god-anim-v1/bones.json); every bone points along its local +Y.
        static readonly Dictionary<string, float> Lengths = new Dictionary<string, float>
        {
            { "hat_01", 1.513f }, { "beard_00", 2.062f }, { "C01_f0_02", .376f },
        };
        static readonly (string bone, float radius)[] Finger = { ("C01_f0_00", .24f), ("C01_f0_01", .20f), ("C01_f0_02", .165f) };

        static StringBuilder log;

        public static void Build()
        {
            log = new StringBuilder();
            try
            {
                AnimSkinMaterial();
                var model = ImportGod();
                var rocks = ImportRocks();
                var controller = BuildController();
                BuildScene(model, rocks, controller);
            }
            catch (Exception e)
            {
                log.AppendLine("ERROR " + e);
            }
            File.WriteAllText("Logs/bridge/god-encounter-build.txt", log.ToString());
            Debug.Log("[GodEncounterBuilder]\n" + log);
        }

        static Material Mat(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) log.AppendLine("ERROR missing material " + path);
            return m;
        }

        // The animated god's skin was rebuilt whole (gm-art/god/anim/rebuild): its own UVs and a baked colour map, smooth
        // (bilinear) rather than the static model's pixel-crisp atlas. Material: a copy of M_MG01_Body with that map.
        static void AnimSkinMaterial()
        {
            string tex = GodDir + "/Textures/T_MG01_BodyAnim_BaseColor.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
            if (ti == null) throw new Exception("no texture " + tex);
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            string mat = GodDir + "/Materials/M_MG01_BodyAnim.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(mat))
                AssetDatabase.CopyAsset(GodDir + "/Materials/M_MG01_Body.mat", mat);
            var m = AssetDatabase.LoadAssetAtPath<Material>(mat);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            m.SetTexture("_BaseMap", t);
            m.SetTexture("_MainTex", t);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            log.AppendLine("skin material " + mat + " <- " + tex);
        }

        static GameObject ImportGod()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            if (mi == null) throw new Exception("no importer for " + Fbx);
            mi.globalScale = 1;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.addCollider = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;
            mi.importAnimation = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.resampleCurves = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var n in new[] { "Body", "BodyAnim", "Cloth", "Leaves" })
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_MG01_" + n), Mat(GodDir + "/Materials/M_MG01_" + n + ".mat"));
            mi.clipAnimations = new ModelImporterClipAnimation[0];
            mi.SaveAndReimport();
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var take in mi.defaultClipAnimations)
            {
                string n = take.takeName;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                if (n.StartsWith("MG01_")) n = n.Substring(5);
                if (Array.IndexOf(Clips, n) < 0) { log.AppendLine("skip take " + take.takeName); continue; }
                take.name = n;
                take.loopTime = Array.IndexOf(Loops, n) >= 0;
                take.loopPose = false;
                clips.Add(take);
                log.AppendLine($"clip {n}: frames {take.firstFrame}–{take.lastFrame}, loop {take.loopTime}");
            }
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
            if (clips.Count != Clips.Length) log.AppendLine($"ERROR {clips.Count} clips of {Clips.Length}");
            return AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        }

        static List<GameObject> ImportRocks()
        {
            var far = Mat(MatDir + "/GM Far.mat");
            var crack = Mat(MatDir + "/GM Crack.mat");
            var list = new List<GameObject>();
            foreach (var s in new[] { "S01", "S02", "S03" })
            {
                string path = $"{RockDir}/MG01_Hand_Rock_{s}.fbx";
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                if (mi == null) { log.AppendLine("ERROR no rock " + path); continue; }
                mi.globalScale = 1;
                mi.useFileScale = true;
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.None;
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.importBlendShapes = false;
                mi.isReadable = true;
                mi.addCollider = false;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var n in new[] { "GM_FarTop", "GM_FarSide", "GM_Top", "GM_Side" })
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), far);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "GM_Crack"), crack);
                mi.SaveAndReimport();
                list.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            }
            log.AppendLine("rocks imported: " + list.Count);
            return list;
        }

        static AnimatorController BuildController()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in new[] { "Notice", "Reach", "Lift" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Trigger);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"))
                .ToDictionary(c => c.name);
            var sm = ctrl.layers[0].stateMachine;
            var st = new Dictionary<string, AnimatorState>();
            float y = 0;
            foreach (var n in Clips)
            {
                var s = sm.AddState(n, new Vector3(300, y += 60, 0));
                if (clips.TryGetValue(n, out var c)) s.motion = c; else log.AppendLine("ERROR no clip " + n);
                st[n] = s;
            }
            sm.defaultState = st["Idle"];
            void On(string from, string to, string trigger, float fade)
            {
                var t = st[from].AddTransition(st[to]);
                t.hasExitTime = false;
                t.hasFixedDuration = true;
                t.duration = fade;
                t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            }
            void After(string from, string to, float fade)
            {
                var t = st[from].AddTransition(st[to]);
                t.hasExitTime = true;
                t.exitTime = 1;
                t.hasFixedDuration = true;
                t.duration = fade;
            }
            On("Idle", "Notice", "Notice", .4f);
            After("Notice", "AlertIdle", .3f);
            On("AlertIdle", "Reach", "Reach", .3f);
            After("Reach", "ReachHold", .3f);
            On("ReachHold", "Lift", "Lift", .4f);
            After("Lift", "Talk", .4f);
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            log.AppendLine("controller: " + ControllerPath);
            return ctrl;
        }

        static void BuildScene(GameObject model, List<GameObject> rockModels, AnimatorController controller)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().First(g => g.name == LevelName).transform;
                var pano = root.Find("Панорама");
                var group = pano.Cast<Transform>().First(t => t.name.StartsWith("MG01"));

                // the old peaks under the hands: replaced by rocks shaped to each hand
                foreach (var t in group.Cast<Transform>().ToArray())
                    if (t.name.StartsWith("Опорная вершина") || t.name == RocksName)
                    {
                        log.AppendLine("removed " + t.name);
                        Object.DestroyImmediate(t.gameObject);
                    }

                // the god: the animated model in place of the static one
                var old = group.Find("MG01");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                inst.transform.SetParent(group, false);
                if (old)
                {
                    inst.transform.localPosition = old.localPosition;
                    inst.transform.localRotation = old.localRotation;
                    inst.transform.localScale = old.localScale;
                    inst.transform.SetSiblingIndex(old.GetSiblingIndex());
                    Object.DestroyImmediate(old.gameObject);
                }
                inst.name = "MG01";
                foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    r.updateWhenOffscreen = true;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                var animator = inst.GetComponent<Animator>();
                if (!animator) animator = inst.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var bones = inst.GetComponentsInChildren<Transform>(true);
                Transform Bone(string n)
                {
                    var b = bones.FirstOrDefault(t => t.name == n);
                    if (!b) log.AppendLine("ERROR no bone " + n);
                    return b;
                }
                // hat and beard on springs
                var springs = inst.GetComponent<GodSpringBones>();
                if (!springs) springs = inst.AddComponent<GodSpringBones>();
                var hat0 = Bone("hat_00");
                var hat1 = Bone("hat_01");
                var beard = Bone("beard_00");
                springs.springs = new[]
                {
                    new GodSpringBones.Spring { bone = hat0, period = 1.7f, damping = .32f, wind = .10f, tailLocal = hat0.InverseTransformPoint(hat1.position) },
                    new GodSpringBones.Spring { bone = hat1, period = 2.3f, damping = .2f, wind = .22f, tailLocal = Vector3.up * Lengths["hat_01"] },
                    new GodSpringBones.Spring { bone = beard, period = 2.5f, damping = .26f, wind = .12f, tailLocal = Vector3.up * Lengths["beard_00"] },
                };
                foreach (var s in springs.springs) log.AppendLine($"spring {s.bone.name}: tail {s.tailLocal}");

                // the walkable finger: a capsule along each bone of the index, as thick as the finger's skin
                var fingerColliders = new List<Collider>();
                for (int i = 0; i < Finger.Length; i++)
                {
                    var b = Bone(Finger[i].bone);
                    Vector3 tail = i + 1 < Finger.Length ? b.InverseTransformPoint(Bone(Finger[i + 1].bone).position)
                        : Vector3.up * Lengths[Finger[i].bone];
                    var cap = b.GetComponent<CapsuleCollider>();
                    if (!cap) cap = b.gameObject.AddComponent<CapsuleCollider>();
                    int axis = Mathf.Abs(tail.x) > Mathf.Abs(tail.y) ? (Mathf.Abs(tail.x) > Mathf.Abs(tail.z) ? 0 : 2) : (Mathf.Abs(tail.y) > Mathf.Abs(tail.z) ? 1 : 2);
                    float r = Finger[i].radius * group.lossyScale.x / b.lossyScale.x;
                    cap.direction = axis;
                    cap.center = tail * .5f;
                    cap.radius = r;
                    cap.height = tail.magnitude + 2 * r;
                    fingerColliders.Add(cap);
                    log.AppendLine($"finger {b.name}: axis {axis}, length {tail.magnitude:F3}, off-axis {(tail - tail[axis] * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward)).magnitude:F4}, radius {r:F3}");
                }

                // rocks under the support hands, built in metres at god coordinates x 100 (pivot: the god's origin)
                var rocks = new GameObject(RocksName).transform;
                rocks.SetParent(group, false);
                rocks.localScale = Vector3.one / group.lossyScale.x;
                foreach (var m in rockModels)
                {
                    var r = (GameObject)PrefabUtility.InstantiatePrefab(m, scene);
                    r.transform.SetParent(rocks, false);
                    foreach (var mf in r.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mc = mf.GetComponent<MeshCollider>();
                        if (!mc) mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
                log.AppendLine("rocks placed: " + rockModels.Count);

                // the linen bubble for his lines
                var oldBubble = scene.GetRootGameObjects().FirstOrDefault(g => g.name == BubbleName);
                if (oldBubble) Object.DestroyImmediate(oldBubble);
                var bubblePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BubblePrefab);
                var bubble = (GameObject)PrefabUtility.InstantiatePrefab(bubblePrefab, scene);
                bubble.name = BubbleName;

                // the meeting
                var oldEnc = root.Find(EncounterName);
                if (oldEnc) Object.DestroyImmediate(oldEnc.gameObject);
                var encGo = new GameObject(EncounterName);
                encGo.transform.SetParent(root, false);
                var enc = encGo.AddComponent<GodEncounter>();
                enc.god = animator;
                enc.head = Bone("head_00");
                enc.finger = Bone("C01_f0_02");
                enc.fingerColliders = fingerColliders.ToArray();
                var marks = root.Find("Метки");
                Transform Mark(string prefix)
                {
                    var t = marks.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith(prefix));
                    if (!t) log.AppendLine("ERROR no mark " + prefix);
                    return t;
                }
                enc.mouth = Mark("M —");
                enc.edgeLine = Mark("E —");
                enc.lip = Mark("Кромка");
                enc.checkpoint = Mark("CP3");
                var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("Игрок"));
                if (!player) log.AppendLine("ERROR no test player");
                var so = new SerializedObject(enc);
                void Ref(string field, Component c)
                {
                    if (!c) { log.AppendLine("ERROR nothing for " + field); return; }
                    so.FindProperty(field).objectReferenceValue = c;
                }
                Component Find(GameObject g, string type) => g ? g.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c && c.GetType().Name == type) : null;
                Ref("motor", Find(player, "FirstPersonMotor"));
                Ref("view", Find(player, "FirstPersonView"));
                Ref("control", Find(player, "PlayerControlGate"));
                Ref("bubble", Find(bubble, "TutorialBubble"));
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(enc);
                EditorUtility.SetDirty(springs);
                EditorUtility.SetDirty(animator);
                log.AppendLine($"marks: mouth {enc.mouth?.position}, edge {enc.edgeLine?.position}, lip {enc.lip?.position}, cp3 {enc.checkpoint?.position}");
                log.AppendLine($"fingertip at rest {enc.finger.position}");

                EditorSceneManager.MarkSceneDirty(scene);
                log.AppendLine(EditorSceneManager.SaveScene(scene) ? "scene saved" : "ERROR scene not saved");
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
