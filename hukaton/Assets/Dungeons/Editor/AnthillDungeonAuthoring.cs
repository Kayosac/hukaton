// ONE-OFF authoring tool (AGENTS.md): run once to create the anthill chambers, kit and test scene as real assets,
// verify, then delete this file and its .meta. The chambers are plain prefabs afterwards — edit them in Unity.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.Dungeons.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.Dungeons.EditorTools
{
    static class AnthillDungeonAuthoring
    {
        const string Dir0 = "Assets/Dungeons/Anthill";
        const string ChamberDir = Dir0 + "/Chambers";
        static readonly StringBuilder Log = new StringBuilder();

        static GameObject P(string name)
        {
            var path = AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/AnthillArt/Prefabs", "Assets/ForestArt/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
            var go = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!go) Log.Append("MISSING ").Append(name).Append(" | ");
            return go;
        }

        sealed class Spec
        {
            public string id; public RoomRole role; public int w, h, rows;
            public List<(int x, int y, Dir d)> doors = new List<(int, int, Dir)>();
            public (int x, int y, Dir d)? wayOut;
            public List<Vector2Int> vents = new List<Vector2Int>();
            public List<(string prefab, Vector3 pos, float yaw, float scale)> props = new List<(string, Vector3, float, float)>();
            public List<(string name, Vector3 pos)> markers = new List<(string, Vector3)>();
            public List<(Vector3 pos, Color color, float range, float intensity, bool spot)> lights = new List<(Vector3, Color, float, float, bool)>();
        }

        static readonly Color Warm = new Color(1f, .62f, .32f);
        static readonly Color Day = new Color(.85f, .9f, 1f);

        static List<Spec> Specs()
        {
            var list = new List<Spec>();
            Spec S(string id, RoomRole role, int w, int h, int rows) { var s = new Spec { id = id, role = role, w = w, h = h, rows = rows }; list.Add(s); return s; }

            var e = S("anthill-entrance", RoomRole.Entrance, 2, 3, 1);
            e.wayOut = (0, 0, Dir.South); e.vents.Add(new Vector2Int(0, 0));
            e.doors.AddRange(new[] { (0, 2, Dir.North), (1, 2, Dir.North), (1, 1, Dir.East), (0, 1, Dir.West) });
            e.props.Add(("AN14_TimberProp", new Vector3(-1.0f, 0, 4.2f), 90, 1));
            e.props.Add(("AN12_WallRoot", new Vector3(5.45f, 0, 2.5f), -90, 1));
            e.props.Add(("AN23_LeafBundle", new Vector3(4.9f, 0, .3f), 20, 1));
            e.props.Add(("FA_D11_LeavesPile", new Vector3(.3f, 0, .2f), 0, 1.4f));
            e.props.Add(("FA_D10_LeavesSparse", new Vector3(1.8f, 0, 2.2f), 70, 1.3f));
            e.props.Add(("FA_PR02_LanternGround", new Vector3(5.1f, 0, 7.4f), 200, 1));
            e.props.Add(("AN38_GlowCluster", new Vector3(-1.0f, 0, 8.6f), 30, 1));
            e.lights.Add((new Vector3(0, 6.5f, -.5f), Day, 11, 4f, true));
            e.markers.Add(("Arrival", new Vector3(1.6f, 0, 1.4f)));
            e.markers.Add(("Spawn", new Vector3(4f, 0, 8f)));

            var j = S("anthill-junction", RoomRole.Junction, 3, 3, 1);
            j.doors.AddRange(new[] { (1, 0, Dir.South), (2, 1, Dir.East), (0, 1, Dir.West), (1, 2, Dir.North) });
            j.props.Add(("AN11_RootPillar", new Vector3(4, 0, 4), 25, 1));
            j.props.Add(("AN19_Handcart", new Vector3(8.2f, 0, -.4f), 35, 1));
            j.props.Add(("FA_PR12_SackLarge", new Vector3(8.8f, 0, 8.6f), 10, 1));
            j.props.Add(("FA_PR11_SackSmall", new Vector3(7.9f, 0, 9.1f), 80, 1));
            j.props.Add(("AN22_Seed", new Vector3(7.2f, 0, 1.2f), 60, 1));
            j.props.Add(("AN38_GlowCluster", new Vector3(-.8f, 0, 8.8f), 0, 1));
            j.props.Add(("AN31_EggClutch", new Vector3(-.8f, 0, -.8f), 40, 1));
            j.props.Add(("FA_PR02_LanternGround", new Vector3(5.6f, 0, 4.9f), 0, 1));
            j.markers.Add(("Spawn", new Vector3(2, 0, 6))); j.markers.Add(("Spawn", new Vector3(6.5f, 0, 2.5f)));

            var b = S("anthill-barracks", RoomRole.Barracks, 4, 4, 2);
            b.doors.AddRange(new[] { (1, 0, Dir.South), (2, 3, Dir.North), (3, 2, Dir.East), (0, 1, Dir.West) });
            b.props.Add(("AN18_Barricade", new Vector3(1.6f, 0, 7.2f), 0, 1));
            b.props.Add(("AN18_Barricade", new Vector3(10.4f, 0, 7.2f), 0, 1));
            b.props.Add(("AN14_TimberProp", new Vector3(-1.1f, 0, 10.8f), 90, 1));
            b.props.Add(("AN14_TimberProp", new Vector3(13.1f, 0, 3.6f), -90, 1));
            b.props.Add(("FA_PR13_CrateClosed", new Vector3(12.6f, 0, 12.6f), 15, 1));
            b.props.Add(("FA_PR13_CrateClosed", new Vector3(11.5f, 0, 12.9f), -10, 1));
            b.props.Add(("FA_PR14_CrateOpen", new Vector3(12.8f, 0, 11.3f), 40, 1));
            b.props.Add(("FA_PR11_SackSmall", new Vector3(-.9f, 0, 12.8f), 0, 1));
            b.props.Add(("FA_PR12_SackLarge", new Vector3(.2f, 0, 13.0f), 50, 1));
            b.props.Add(("AN30_Chain", new Vector3(6, 7.9f, 9), 0, 1));
            b.props.Add(("FA_PR02_LanternGround", new Vector3(6, 0, 9.6f), 0, 1));
            b.props.Add(("FA_PR02_LanternGround", new Vector3(-.9f, 0, -.9f), 0, 1));
            b.props.Add(("AN38_GlowCluster", new Vector3(12.8f, 0, -.8f), 0, 1));
            b.markers.Add(("Spawn MachineGun", new Vector3(6, 0, 9.2f)));
            b.markers.Add(("Spawn", new Vector3(2.5f, 0, 10.5f))); b.markers.Add(("Spawn", new Vector3(9.5f, 0, 10.5f))); b.markers.Add(("Spawn", new Vector3(6, 0, 5.5f)));

            var st = S("anthill-store", RoomRole.Store, 2, 2, 1);
            st.doors.Add((0, 0, Dir.South));
            st.props.Add(("FA_PR14_CrateOpen", new Vector3(-1.0f, 0, 5.1f), 5, 1));
            st.props.Add(("FA_PR13_CrateClosed", new Vector3(5.0f, 0, 5.2f), -12, 1));
            st.props.Add(("FA_PR12_SackLarge", new Vector3(5.2f, 0, 3.6f), 30, 1));
            st.props.Add(("FA_PR11_SackSmall", new Vector3(4.6f, 0, 2.6f), 120, 1));
            st.props.Add(("FA_PR09_PotLow", new Vector3(-1.1f, 0, 3.4f), 0, 1));
            st.props.Add(("FA_PR09_PotLow", new Vector3(-.6f, 0, 2.9f), 40, .8f));
            st.props.Add(("AN21_WalnutBowl", new Vector3(5.0f, 0, -.6f), 200, 1));
            st.props.Add(("AN22_Seed", new Vector3(3.6f, 0, -.9f), 15, 1));
            st.props.Add(("AN22_Seed", new Vector3(4.1f, 0, 1.0f), 100, .9f));
            st.props.Add(("AN26_Parcel", new Vector3(-1.0f, 0, 1.2f), 70, 1));
            st.props.Add(("AN26_Parcel", new Vector3(-1.1f, .37f, 1.25f), 20, .8f));
            st.props.Add(("AN23_LeafBundle", new Vector3(1.0f, 0, 5.3f), 0, 1));
            st.props.Add(("FA_PR02_LanternGround", new Vector3(3.2f, 0, 5.3f), 0, 1));
            st.markers.Add(("Reward", new Vector3(2.0f, 0, 4.6f)));
            st.markers.Add(("Spawn", new Vector3(2, 0, 1.5f)));

            var n = S("anthill-nursery", RoomRole.Nursery, 3, 3, 1);
            n.doors.AddRange(new[] { (1, 0, Dir.South), (1, 2, Dir.North), (2, 1, Dir.East), (0, 1, Dir.West) });
            foreach (var (x, z, yaw) in new[] { (-.4f, -.4f, 10f), (8.4f, -.4f, 80f), (-.4f, 8.4f, 200f), (8.4f, 8.4f, 300f) })
            {
                n.props.Add(("AN32_LeafNest", new Vector3(x, 0, z), yaw, 1));
                n.props.Add(("AN31_EggClutch", new Vector3(x, .12f, z), yaw + 40, .9f));
            }
            n.props.Add(("AN31_Egg", new Vector3(8.6f, 0, 2.6f), 0, 1));
            n.props.Add(("AN38_GlowCluster", new Vector3(1.6f, 0, -.9f), 0, 1));
            n.props.Add(("AN38_GlowCluster", new Vector3(9.0f, 0, 6.4f), 90, 1));
            n.props.Add(("AN37_LanternMushroom", new Vector3(6.6f, 0, 6.6f), 30, 1));
            n.props.Add(("AN35_ResinPatch", new Vector3(-1.4f, 1.6f, 4.0f), 90, 1));
            n.markers.Add(("Spawn Larva", new Vector3(1.5f, 0, 1.5f))); n.markers.Add(("Spawn Larva", new Vector3(6.5f, 0, 6.5f)));
            n.markers.Add(("Spawn", new Vector3(4, 0, 4)));

            var f = S("anthill-farm", RoomRole.Farm, 5, 6, 2);
            f.doors.Add((2, 0, Dir.South));
            foreach (float x in new[] { 1.8f, 14.2f })
                foreach (float z in new[] { 5f, 10.5f, 16f })
                    f.props.Add(("AN40_CultivationBed", new Vector3(x, 0, z), 0, 1));
            f.props.Add(("AN41_CompostMound", new Vector3(.2f, 0, 20.2f), 30, 1));
            f.props.Add(("AN41_CompostMound", new Vector3(15.8f, 0, 20.2f), -20, 1));
            f.props.Add(("AN37_LanternMushroom", new Vector3(8f, 0, 20.4f), 0, 1.15f));
            f.props.Add(("AN37_LanternMushroom", new Vector3(-.8f, 0, 1.2f), 120, .9f));
            f.props.Add(("AN37_LanternMushroom", new Vector3(16.8f, 0, 12.8f), 240, 1));
            f.props.Add(("AN38_GlowCluster", new Vector3(5.2f, 0, 8f), 0, 1));
            f.props.Add(("AN38_GlowCluster", new Vector3(10.8f, 0, 13.4f), 0, 1));
            f.props.Add(("AN19_Handcart", new Vector3(8.4f, 0, 15.6f), 170, 1));
            f.props.Add(("FA_PR12_SackLarge", new Vector3(6.6f, 0, 20.6f), 0, 1));
            f.props.Add(("FA_PR11_SackSmall", new Vector3(9.8f, 0, 20.8f), 0, 1));
            f.markers.Add(("Reward", new Vector3(8, 0, 19)));
            f.markers.Add(("Spawn", new Vector3(8, 0, 8))); f.markers.Add(("Spawn", new Vector3(8, 0, 13)));

            var q = S("anthill-queen", RoomRole.Queen, 5, 5, 2);
            q.doors.Add((2, 0, Dir.South));
            q.vents.Add(new Vector2Int(0, 4));
            q.props.Add(("AN33_QueenBed", new Vector3(8, 0, 11.5f), 180, 1));
            q.props.Add(("AN34_BurgundyCloth", new Vector3(8, .25f, 13.2f), 180, 1));
            q.props.Add(("AN13_RootButtress", new Vector3(8, 0, 16.4f), 0, 1));
            q.props.Add(("AN32_LeafNest", new Vector3(1.4f, 0, 12.5f), 30, 1));
            q.props.Add(("AN31_EggClutch", new Vector3(1.4f, .12f, 12.5f), 0, 1));
            q.props.Add(("AN32_LeafNest", new Vector3(14.6f, 0, 12.5f), 160, 1));
            q.props.Add(("AN31_EggClutch", new Vector3(14.6f, .12f, 12.5f), 70, 1));
            q.props.Add(("AN11_RootPillar", new Vector3(-.4f, 0, 4.4f), 0, 1));
            q.props.Add(("AN11_RootPillar", new Vector3(16.4f, 0, 4.4f), 180, 1));
            q.props.Add(("AN37_LanternMushroom", new Vector3(-.6f, 0, -.6f), 0, 1));
            q.props.Add(("AN37_LanternMushroom", new Vector3(16.6f, 0, -.6f), 90, 1));
            q.props.Add(("AN30_Chain", new Vector3(4.5f, 7.9f, 7), 0, 1));
            q.props.Add(("AN30_Chain", new Vector3(11.5f, 7.9f, 7), 0, 1));
            q.props.Add(("AN35_ResinPatch", new Vector3(17.4f, 2.2f, 9f), -90, 1.2f));
            q.markers.Add(("Spawn Boss", new Vector3(8, .25f, 11.5f)));
            q.markers.Add(("Exit", new Vector3(0, 0, 16)));
            q.lights.Add((new Vector3(8, 6, 11), new Color(.95f, .55f, .5f), 16, 2.5f, false));
            return list;
        }

        static string Author()
        {
            Log.Clear();
            Directory.CreateDirectory(ChamberDir);
            var chambers = new List<ChamberTemplate>();
            foreach (var spec in Specs()) chambers.Add(MakeChamber(spec));

            string kitPath = Dir0 + "/Anthill Kit.asset";
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(kitPath);
            if (!kit) { kit = ScriptableObject.CreateInstance<DungeonKit>(); AssetDatabase.CreateAsset(kit, kitPath); }
            kit.walls = new[] { P("AN01_EarthWall") };
            kit.corner = P("AN02_EarthCorner");
            kit.doorway = P("AN04_TunnelCollar");
            kit.vault = P("AN03_CeilingVault");
            kit.floors = new[] { P("AN06_EarthFloor") };
            kit.ramp = P("AN07_EarthRamp");
            kit.ventCollar = P("AN10_VentCollar");
            kit.chambers = chambers;
            kit.corridorLight = P("FA_PR03_LanternBracket");
            kit.corridorLightEvery = 2;
            kit.corridorLightColor = Warm; kit.corridorLightRange = 10; kit.corridorLightIntensity = 3f;
            kit.ambient = new Color(.46f, .38f, .32f); kit.fogColor = new Color(.11f, .08f, .065f); kit.fogDensity = .018f;
            kit.wallDressing = new List<DungeonKit.Dressing>
            {
                new DungeonKit.Dressing { prefab = P("AN12_WallRoot"), chance = .14f, yawJitter = 10, scaleRange = new Vector2(.85f, 1.1f) },
                new DungeonKit.Dressing { prefab = P("AN39_WallFungi"), chance = .1f, yawJitter = 10, scaleRange = new Vector2(.9f, 1.2f) },
                new DungeonKit.Dressing { prefab = P("AN35_ResinPatch"), chance = .06f, yawJitter = 6, scaleRange = new Vector2(.8f, 1.1f) },
            };
            kit.corridorFloorDressing = new List<DungeonKit.Dressing>
            {
                new DungeonKit.Dressing { prefab = P("AN38_GlowCluster"), chance = .12f, scaleRange = new Vector2(.8f, 1.1f) },
                new DungeonKit.Dressing { prefab = P("AN22_Seed"), chance = .06f, scaleRange = new Vector2(.8f, 1.2f) },
                new DungeonKit.Dressing { prefab = P("FA_D10_LeavesSparse"), chance = .12f, scaleRange = new Vector2(1f, 1.4f) },
            };
            kit.ceilingDressing = new List<DungeonKit.Dressing>
            {
                new DungeonKit.Dressing { prefab = P("AN30_Chain"), chance = .04f },
            };
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            MakeTestScene(kit);
            return "OK " + Log;
        }

        /// <summary>The way in on the surface: the mound with its throat; wired into the endless world's content.</summary>
        static string AuthorEntrance()
        {
            Log.Clear();
            var root = new GameObject("Anthill Entrance");
            var entrance = root.AddComponent<Hakaton.WorldAssembly.Unity.DungeonEntrance>();
            var mound = (GameObject)PrefabUtility.InstantiatePrefab(P("AN09_EntranceMound"), root.transform);
            mound.transform.localPosition = Vector3.zero;
            // The throat runs from the mouth (+Z, z 3.4) down into the heap to its dark end (z -1.4, 0.7 m down).
            var inside = new GameObject("Inside").transform; inside.SetParent(root.transform, false); inside.localPosition = new Vector3(.4f, -.6f, -.6f);
            var outside = new GameObject("Outside").transform; outside.SetParent(root.transform, false); outside.localPosition = new Vector3(.4f, 0, 5.8f);
            entrance.outside = outside;   // (the walk-in passage replaced the throat: AnthillRework.Entrance)
            string path = Dir0 + "/Anthill Entrance.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            var content = AssetDatabase.LoadAssetAtPath<Hakaton.WorldAssembly.Unity.WorldContent>("Assets/WorldAssembly/World content.asset");
            if (!content) return "FAILED no World content";
            content.dungeonEntrance = saved.GetComponent<Hakaton.WorldAssembly.Unity.DungeonEntrance>();
            content.anthillKit = AssetDatabase.LoadAssetAtPath<DungeonKit>(Dir0 + "/Anthill Kit.asset");
            EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();
            return "OK entrance " + path + " kit=" + (content.anthillKit ? "set" : "MISSING") + " " + Log;
        }

        static ChamberTemplate MakeChamber(Spec s)
        {
            var root = new GameObject(s.id);
            var t = root.AddComponent<ChamberTemplate>();
            t.id = s.id; t.role = s.role; t.rows = s.rows;
            t.cells = new List<Vector2Int>();
            for (int x = 0; x < s.w; x++) for (int y = 0; y < s.h; y++) t.cells.Add(new Vector2Int(x, y));
            t.doors = s.doors.Select(d => new ChamberTemplate.Door { cell = new Vector2Int(d.x, d.y), side = d.d }).ToList();
            t.hasWayOut = s.wayOut.HasValue;
            if (s.wayOut.HasValue) t.wayOut = new ChamberTemplate.Door { cell = new Vector2Int(s.wayOut.Value.x, s.wayOut.Value.y), side = s.wayOut.Value.d };
            t.ventCells = s.vents;

            var props = new GameObject("Props").transform; props.SetParent(root.transform, false);
            foreach (var (name, pos, yaw, scale) in s.props)
            {
                var prefab = P(name);
                if (!prefab) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, props);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                go.transform.localScale = Vector3.one * scale;
                if (name.Contains("Lantern")) s.lights.Add((pos + Vector3.up * 1.1f, Warm, 12, 3.2f, false));
            }
            if (s.lights.Count > 0)
            {
                var lights = new GameObject("Lights").transform; lights.SetParent(root.transform, false);
                foreach (var (pos, color, range, intensity, spot) in s.lights)
                {
                    var l = new GameObject(spot ? "Daylight Shaft" : "Glow").AddComponent<Light>();
                    l.transform.SetParent(lights, false);
                    l.transform.localPosition = pos;
                    l.type = spot ? LightType.Spot : LightType.Point;
                    if (spot) { l.transform.localRotation = Quaternion.Euler(90, 0, 0); l.spotAngle = 70; }
                    l.color = color; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
                }
            }
            if (s.markers.Count > 0)
            {
                var marks = new GameObject("Markers").transform; marks.SetParent(root.transform, false);
                foreach (var (name, pos) in s.markers)
                {
                    var m = new GameObject(name).transform; m.SetParent(marks, false); m.localPosition = pos;
                }
            }
            string path = $"{ChamberDir}/{s.id}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<ChamberTemplate>();
        }

        static void MakeTestScene(DungeonKit kit)
        {
            string path = "Assets/Dungeons/Scenes/Anthill Dungeon Test.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("Anthill Dungeon");
            SceneManager.MoveGameObjectToScene(go, scene);
            var builder = go.AddComponent<DungeonBuilder>();
            builder.kit = kit; builder.buildOnStart = true;
            go.AddComponent<DungeonCulling>();
            var cam = new GameObject("Camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(2, 1.7f, 2);
            cam.backgroundColor = new Color(.02f, .015f, .012f); cam.clearFlags = CameraClearFlags.SolidColor;
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid()) SceneManager.SetActiveScene(active);
        }
    }
}
