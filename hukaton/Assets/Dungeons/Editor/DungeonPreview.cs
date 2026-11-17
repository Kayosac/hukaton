// Development aid (temporary, removed with the authoring tools): builds a dungeon in a throw-away additive scene and
// renders an overview and eye-level shots to Logs/bridge/dungeon/*.png. No project scene is changed.
using System.IO;
using System.Linq;
using System.Text;
using Hakaton.Dungeons.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Hakaton.Dungeons.EditorTools
{
    static class DungeonPreview
    {
        static string Shots(string arg)
        {
            var parts = (arg ?? "").Split(' ');
            int seed = parts.Length > 0 && int.TryParse(parts[0], out var s0) ? s0 : 1;
            bool dark = !(parts.Length > 1 && parts[1] == "lit");
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>("Assets/Dungeons/Anthill/Anthill Kit.asset");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "dungeon");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // Lights of the scenes the user has open would leak into the shots (their sun through the vault seams).
            var foreign = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled).ToList();
            foreach (var l in foreign) l.enabled = false;
            var saved = (RenderSettings.fog, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.fogMode, RenderSettings.ambientMode, RenderSettings.ambientLight);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Preview Dungeon");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = new Vector3(0, -2000, 0);
                var builder = root.AddComponent<DungeonBuilder>();
                builder.kit = kit;
                var plan = builder.Build(seed);
                log.Append($"seed {seed} large={plan.Large} levels={plan.Levels} rooms={plan.Rooms.Count} cells={plan.Cells.Count} ");
                log.Append("rooms: " + string.Join(",", plan.Rooms.Select(r => r.Shape.Role + "@" + r.Floor)) + " ");
                int tris = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += (int)mf.sharedMesh.GetIndexCount(0) / 3;
                log.Append($"tris~{tris} ");

                var camGo = new GameObject("Preview Camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = .05f; cam.farClipPlane = 600;
                var rt = new RenderTexture(1600, 1000, 24);
                cam.targetTexture = rt;

                // Overview from above, lit.
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.55f, .5f, .45f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
                sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(60, 30, 0);
                var b = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                // Lift the ceilings out of the way for the overview: hide vaults.
                var vaults = root.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("AN03") || t.name.StartsWith("AN10")).Select(t => t.gameObject).ToList();
                foreach (var v in vaults) v.SetActive(false);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.12f, .1f, .09f);
                cam.orthographic = true; cam.orthographicSize = Mathf.Max(b.extents.z, b.extents.x / 1.6f) + 4;
                cam.transform.position = b.center + Vector3.up * 200; cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                Save(cam, rt, Path.Combine(dir, $"s{seed}_overview.png"));
                cam.orthographic = false; cam.fieldOfView = 50;
                cam.transform.position = b.center + new Vector3(0, b.extents.magnitude * 1.1f, -b.extents.magnitude * 1.2f);
                cam.transform.LookAt(b.center);
                Save(cam, rt, Path.Combine(dir, $"s{seed}_perspective.png"));
                foreach (var v in vaults) v.SetActive(true);

                // Eye-level shots in each room, dark like the real thing.
                if (dark)
                {
                    sun.intensity = .0f;
                    RenderSettings.ambientLight = kit.ambient;
                    RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = kit.fogDensity;
                    RenderSettings.fogColor = kit.fogColor;
                    cam.backgroundColor = RenderSettings.fogColor;
                }
                cam.fieldOfView = 75;
                int i = 0;
                foreach (var room in plan.Rooms)
                {
                    var door = room.Doors[room.UsedDoors.FirstOrDefault()];
                    var inside = root.transform.TransformPoint(DungeonGeometry.Center(door.cell, room.Floor) - DungeonGeometry.Offset(door.side) * 1.2f + Vector3.up * 1.7f);
                    var center = Vector3.zero; foreach (var c in room.Cells) center += DungeonGeometry.Center(c, room.Floor); center /= room.Cells.Count;
                    cam.transform.position = inside;
                    cam.transform.LookAt(root.transform.TransformPoint(center + Vector3.up * 1.2f));
                    Save(cam, rt, Path.Combine(dir, $"s{seed}_room{i++}_{room.Shape.Role}.png"));
                }
                // A corridor view.
                if (plan.CorridorCells.Count > 3)
                {
                    var c0 = plan.CorridorCells[1]; var c1 = plan.CorridorCells[3];
                    cam.transform.position = root.transform.TransformPoint(DungeonGeometry.Center(c0, plan.Cells[c0].Floor) + Vector3.up * 1.7f);
                    cam.transform.LookAt(root.transform.TransformPoint(DungeonGeometry.Center(c1, plan.Cells[c1].Floor) + Vector3.up * 1.4f));
                    Save(cam, rt, Path.Combine(dir, $"s{seed}_corridor.png"));
                }
                cam.targetTexture = null; Object.DestroyImmediate(rt);
                builder.Clear();
            }
            finally
            {
                (RenderSettings.fog, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.fogMode, RenderSettings.ambientMode, RenderSettings.ambientLight) = saved;
                if (active.IsValid()) SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
                foreach (var l in foreign) if (l) l.enabled = true;
            }
            return log.ToString();
        }

        static void Save(Camera cam, RenderTexture rt, string file)
        {
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
