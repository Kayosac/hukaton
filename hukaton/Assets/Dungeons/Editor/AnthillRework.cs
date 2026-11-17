// One-off authoring (temporary, removed with the other authoring tools before delivery): the walk-in entrance hill
// prefab and the kit's module variants and backstop.
using System.Collections.Generic;
using System.Linq;
using Hakaton.Dungeons.Unity;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Dungeons.EditorTools
{
    static class AnthillRework
    {
        const string Art = "Assets/AnthillArt/Prefabs";
        const string KitPath = "Assets/Dungeons/Anthill/Anthill Kit.asset";
        const string EntrancePath = "Assets/Dungeons/Anthill/Anthill Entrance.prefab";

        static GameObject P(string group, string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Art}/{group}/{name}.prefab");

        static GameObject[] Variants(string group, string name)
        {
            var list = new List<GameObject>();
            foreach (var suffix in new[] { "", "_B", "_C", "_D" })
            {
                var p = P(group, name + suffix);
                if (p) list.Add(p);
            }
            return list.ToArray();
        }

        static string Kit(string arg)
        {
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            kit.walls = Variants("Shell", "AN01_EarthWall");
            kit.corners = Variants("Shell", "AN02_EarthCorner");
            kit.vaults = Variants("Shell", "AN03_CeilingVault");
            kit.floors = Variants("Shell", "AN06_EarthFloor");
            if (kit.corners.Length > 0) kit.corner = kit.corners[0];
            if (kit.vaults.Length > 0) kit.vault = kit.vaults[0];
            kit.backstop = AssetDatabase.LoadAssetAtPath<Material>("Assets/AnthillArt/Materials/M_Ant_Earth.mat");
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return $"walls {kit.walls.Length} corners {kit.corners.Length} vaults {kit.vaults.Length} floors {kit.floors.Length} backstop {(kit.backstop ? kit.backstop.name : "none")}";
        }

        static Material Veil(string name, bool additive)
        {
            string path = $"Assets/AnthillArt/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_Cull", 2);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", additive ? Color.white : Color.black);
            EditorUtility.SetDirty(m);
            return m;
        }

        static string Materials(string arg)
        {
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            kit.passageDark = Veil("M_Ant_PassageDark", false);
            kit.passageGlow = Veil("M_Ant_PassageGlow", true);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return $"dark {kit.passageDark.shader.name}, glow {kit.passageGlow.shader.name}";
        }

        /// <summary>The hill alone in a throw-away scene, lit from the front, from a few places: Logs/bridge/hill/*.png.</summary>
        static string HillShot(string arg)
        {
            var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "bridge", "hill");
            System.IO.Directory.CreateDirectory(dir);
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            var foreign = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled).ToList();
            foreach (var l in foreign) l.enabled = false;
            var log = new System.Text.StringBuilder();
            try
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                var hill = (GameObject)PrefabUtility.InstantiatePrefab(P("Entrance", "AN09_EntranceHill"), scene);
                hill.transform.position = new Vector3(0, -3000, 0);
                foreach (var mr in hill.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    var m = mf.sharedMesh;
                    var uv = new List<Vector2>(); m.GetUVs(0, uv);
                    var n = new List<Vector3>(); m.GetNormals(n);
                    log.Append($"{mr.name}: v{m.vertexCount} t{m.triangles.Length / 3} uv0[{(uv.Count > 0 ? uv.Min(u => u.x).ToString("0.0") + ".." + uv.Max(u => u.x).ToString("0.0") : "none")}] mat {mr.sharedMaterial.name} sub{m.subMeshCount}; ");
                }
                var sun = new GameObject("Sun").AddComponent<Light>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
                sun.type = LightType.Directional; sun.intensity = 1.3f; sun.transform.rotation = Quaternion.Euler(40, 20, 0);
                var cam = new GameObject("Cam").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.75f, .74f, .7f); cam.fieldOfView = 60;
                var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                void Shot(string name, Vector3 at, Vector3 look)
                {
                    cam.transform.position = hill.transform.TransformPoint(at);
                    cam.transform.LookAt(hill.transform.TransformPoint(look));
                    cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                }
                Shot("front15", new Vector3(0, 1.7f, -15), new Vector3(0, 3, 4));
                Shot("side45", new Vector3(-11, 1.7f, -10), new Vector3(0, 3, 4));
                Shot("close", new Vector3(3, 2.5f, -6), new Vector3(3, 3, 4));
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            finally
            {
                if (active.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                foreach (var l in foreign) if (l) l.enabled = true;
            }
            return log.ToString();
        }

        static string Entrance(string arg)
        {
            var root = PrefabUtility.LoadPrefabContents(EntrancePath);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var hillPrefab = P("Entrance", "AN09_EntranceHill");
                if (hillPrefab)
                {
                    var hill = (GameObject)PrefabUtility.InstantiatePrefab(hillPrefab, root.transform);
                    hill.name = "Hill";
                    hill.transform.localPosition = Vector3.zero;
                    hill.transform.localRotation = Quaternion.identity;
                }
                var passage = new GameObject("Passage").transform;
                passage.SetParent(root.transform, false);
                var outside = new GameObject("Outside").transform;
                outside.SetParent(root.transform, false);
                outside.localPosition = new Vector3(0, 0, -7f);
                var entrance = root.GetComponent<DungeonEntrance>();
                entrance.passage = passage;
                entrance.outside = outside;
                entrance.clearCentre = new Vector3(0, 0, 5f);
                entrance.clearRadius = 12.5f;
                // The hill has its own tunnel (3 m wide, 3.4 m tall at the mouth, narrowing to 2.7 x 3.05 at 9 m).
                entrance.layModules = !hillPrefab;
                entrance.veilHalfWidth = 1.55f; entrance.veilHeight = 3.45f; entrance.veilFrom = .8f; entrance.veilTo = 8.8f;
                PrefabUtility.SaveAsPrefabAsset(root, EntrancePath);
                return "entrance: hill " + (hillPrefab ? hillPrefab.name : "none yet");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
