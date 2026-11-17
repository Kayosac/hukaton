// Development aid (temporary, removed with the authoring tools): looks for holes in a built dungeon. Renders from the
// middle of cells in six directions against a magenta background (no fog, flat light) and reports where the magenta
// shows through, saving the worst views to Logs/bridge/seams.
using System.Collections.Generic;
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
    static class DungeonSeams
    {
        static string Scan(string arg)
        {
            var parts = (arg ?? "").Split(' ');
            int seed = parts.Length > 0 && int.TryParse(parts[0], out var s0) ? s0 : 1;
            int keep = parts.Length > 1 && int.TryParse(parts[1], out var k0) ? k0 : 16;
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>("Assets/Dungeons/Anthill/Anthill Kit.asset");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge", "seams");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            var log = new StringBuilder();
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var foreign = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled).ToList();
            foreach (var l in foreign) l.enabled = false;
            var saved = (RenderSettings.fog, RenderSettings.ambientMode, RenderSettings.ambientLight);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Seam Dungeon");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = new Vector3(0, -2000, 0);
                var builder = root.AddComponent<DungeonBuilder>();
                builder.kit = kit;
                var plan = builder.Build(seed);
                int tris = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += (int)mf.sharedMesh.GetIndexCount(0) / 3;
                // Lights off: we only want silhouettes against the background.
                foreach (var l in root.GetComponentsInChildren<Light>()) l.enabled = false;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.5f, .45f, .4f);
                var camGo = new GameObject("Seam Camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = .03f; cam.farClipPlane = 400; cam.fieldOfView = 90;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.magenta;
                var rt = new RenderTexture(192, 192, 24);
                var big = new RenderTexture(640, 640, 24);
                cam.targetTexture = rt;
                var tex = new Texture2D(192, 192, TextureFormat.RGB24, false);
                var hits = new List<(int count, Vector3 pos, Quaternion rot, string where)>();
                var dirs = new (string name, Quaternion rot)[]
                {
                    ("N", Quaternion.Euler(0, 0, 0)), ("E", Quaternion.Euler(0, 90, 0)), ("S", Quaternion.Euler(0, 180, 0)), ("W", Quaternion.Euler(0, 270, 0)),
                    ("Up", Quaternion.Euler(-80, 0, 0)), ("NE-up", Quaternion.Euler(-35, 45, 0)), ("SW-up", Quaternion.Euler(-35, 225, 0)),
                };
                int cells = 0, leaky = 0;
                foreach (var kv in plan.Cells)
                {
                    cells++;
                    var info = kv.Value;
                    var at = root.transform.TransformPoint(DungeonGeometry.Center(kv.Key, info.Floor + (info.Kind == CellKind.Ramp ? .5f : 0)) + Vector3.up * 1.7f);
                    int worst = 0; string wn = null; Quaternion wr = default;
                    int total = 0;
                    foreach (var (name, rot) in dirs)
                    {
                        cam.transform.SetPositionAndRotation(at, root.transform.rotation * rot);
                        cam.Render();
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, 192, 192), 0, 0); tex.Apply();
                        RenderTexture.active = prev;
                        int n = 0;
                        foreach (var p in tex.GetPixels32()) if (p.r > 200 && p.g < 40 && p.b > 200) n++;
                        total += n;
                        if (n > worst) { worst = n; wn = name; wr = root.transform.rotation * rot; }
                    }
                    if (total > 0)
                    {
                        leaky++;
                        string vent = info.Kind == CellKind.Room ? "room" : info.Kind.ToString().ToLower();
                        hits.Add((worst, at, wr, $"{kv.Key.X},{kv.Key.Y} {vent} {wn} px={total}"));
                    }
                }
                log.Append($"seed {seed} cells={cells} leaky={leaky} tris~{tris} | ");
                cam.targetTexture = big;
                var bt = new Texture2D(640, 640, TextureFormat.RGB24, false);
                int i = 0;
                foreach (var h in hits.OrderByDescending(h => h.count).Take(keep))
                {
                    cam.transform.SetPositionAndRotation(h.pos, h.rot);
                    cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = big;
                    bt.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); bt.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, $"{i++:00} {h.where.Replace(',', '_')}.png"), bt.EncodeToPNG());
                }
                foreach (var h in hits.OrderByDescending(h => h.count).Take(40)) log.Append(h.where).Append("; ");
                cam.targetTexture = null;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(big); Object.DestroyImmediate(tex); Object.DestroyImmediate(bt);
                builder.Clear();
            }
            finally
            {
                (RenderSettings.fog, RenderSettings.ambientMode, RenderSettings.ambientLight) = saved;
                if (active.IsValid()) SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
                foreach (var l in foreign) if (l) l.enabled = true;
            }
            return log.ToString();
        }
    }
}
