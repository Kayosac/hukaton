using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.GodMountains.Editor
{
    /// <summary>
    /// Looks at the god's mountains the way the player will: opens the level next to whatever is open, stands a camera
    /// on the route at given metres (eye height, looking along the route), applies the air of that place, optionally
    /// plays a Retro Arsenal effect at a point and marks points with bright balls, renders to PNG in
    /// output/god-mountains-moments/shots. Nothing in the scene is saved.
    /// Run from the menu, or by writing Temp/god-shots.request (one shot per line:
    /// name metres [yaw pitch fov] [effect=guid@x,y,z@sim] [mark=x,y,z] ...; '#' comments).
    /// </summary>
    [InitializeOnLoad]
    static class GodMomentShots
    {
        const string ScenePath = "Assets/GodMountains/Scenes/GodMountains.unity";
        const string Request = "Temp/god-shots.request";
        static double next;

        static GodMomentShots() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1;
            // Temp/god-refresh.request: pick up changed files (the editor does not while it is in the background)
            if (File.Exists("Temp/god-refresh.request") && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                File.Delete("Temp/god-refresh.request");
                AssetDatabase.Refresh();
                return;
            }
            if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string text = File.ReadAllText(Request);
            File.Delete(Request);
            string log = OutDir + "/log.txt";
            try { File.WriteAllText(log, Capture(text)); }
            catch (Exception e) { File.WriteAllText(log, "ERROR\n" + e); }
        }

        [MenuItem("Hakaton/God mountains/Shots of the path")]
        static void Menu() => Debug.Log(Capture("start 2\nmid 90\nshelf 120\nmouth 168"));

        static string OutDir
        {
            get
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/god-mountains-moments/shots"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string[] AppendMark(string[] parts, Vector3 at)
        {
            var list = new List<string>(parts) { $"mark={at.x.ToString(CultureInfo.InvariantCulture)},{at.y.ToString(CultureInfo.InvariantCulture)},{at.z.ToString(CultureInfo.InvariantCulture)}" };
            return list.ToArray();
        }

        static float Num(string[] parts, int i, float otherwise) =>
            parts.Length > i && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : otherwise;

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static Vector3 V(string s)
        {
            var p = s.Split(',');
            return new Vector3(F(p[0]), F(p[1]), F(p[2]));
        }

        static string Capture(string request)
        {
            var report = new System.Text.StringBuilder();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
            var wasActive = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var temp = new List<UnityEngine.Object>();
            try
            {
                GodMountainsLevel level = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out level)) break;
                var air = level.atmosphere;
                Physics.SyncTransforms();
                var camGo = new GameObject("god shots camera") { hideFlags = HideFlags.HideAndDontSave };
                temp.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = .05f;
                var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 1 };
                temp.Add(rt);
                cam.targetTexture = rt;
                foreach (var raw in request.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string name = parts[0];
                    float metres = Num(parts, 1, 0);
                    float yaw = Num(parts, 2, 0), pitch = Num(parts, 3, 0), fov = Num(parts, 4, 70);
                    Vector3 eye = RoutePoint(air, metres) + Vector3.up * 1.62f;
                    Vector3 fwd = RouteForward(air, metres + 1.5f);
                    var marks = new List<Vector3>();
                    foreach (var p in parts)
                        if (p.StartsWith("find="))
                        {
                            report.Append(name + ": find " + p.Substring(5) + " →");
                            foreach (var root in scene.GetRootGameObjects())
                                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                                    if (t.name.Contains(p.Substring(5)))
                                        report.Append($" [{t.name} @ {t.position.x:F1},{t.position.y:F1},{t.position.z:F1} parent {(t.parent ? t.parent.name : "-")}]");
                            report.AppendLine();
                        }
                    foreach (var p in parts)
                    {
                        if (!p.StartsWith("moment=")) continue;
                        GodMoment moment = null;
                        foreach (var m in level.GetComponentsInChildren<GodMoment>(true))
                            if (m.GetType().Name == p.Substring(7)) moment = m;
                        if (!moment) { report.AppendLine(name + ": no moment " + p); continue; }
                        metres = moment.atMetres;
                        var view = moment.PreviewView();
                        eye = view.eye;
                        fwd = view.forward;
                        moment.PreviewPoints(marks);
                        report.Append(name + ": points");
                        foreach (var mk in marks) report.Append($" {mk.x:F2},{mk.y:F2},{mk.z:F2}");
                        report.AppendLine();
                    }
                    cam.transform.position = eye;
                    cam.transform.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(-pitch, yaw, 0);
                    if (Array.IndexOf(parts, "clean") < 0) foreach (var mk in marks) parts = AppendMark(parts, mk);
                    cam.fieldOfView = fov;
                    var a = air.AirAt(metres);
                    air.viewCamera = cam;
                    air.Apply(a);
                    cam.farClipPlane = Mathf.Clamp(air.fogCutoff / Mathf.Max(a.density, 1e-6f), air.minFar, air.maxFar);
                    var spawned = new List<GameObject>();
                    foreach (var p in parts)
                    {
                        if (p.StartsWith("mark="))
                        {
                            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            ball.hideFlags = HideFlags.HideAndDontSave;
                            ball.transform.position = V(p.Substring(5));
                            ball.transform.localScale = Vector3.one * .25f;
                            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.magenta };
                            temp.Add(m);
                            ball.GetComponent<Renderer>().sharedMaterial = m;
                            UnityEngine.Object.DestroyImmediate(ball.GetComponent<Collider>());
                            spawned.Add(ball);
                        }
                        else if (p.StartsWith("effect="))
                        {
                            var e = p.Substring(7).Split('@');
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(e[0]));
                            if (!prefab) { report.AppendLine(name + ": no effect " + e[0]); continue; }
                            var fx = UnityEngine.Object.Instantiate(prefab, V(e[1]), Quaternion.identity);
                            fx.hideFlags = HideFlags.HideAndDontSave;
                            if (e.Length > 3) fx.transform.localScale *= F(e[3]);
                            float sim = e.Length > 2 ? F(e[2]) : .6f;
                            if (e.Length > 4)
                            {
                                var c = e[4].Split(',');
                                var tint = new Color(F(c[0]), F(c[1]), F(c[2]), F(c[3]));
                                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.startColor = tint; }
                            }
                            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(sim, true, true);
                            spawned.Add(fx);
                        }
                    }
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    foreach (var s in spawned) UnityEngine.Object.DestroyImmediate(s);
                    report.AppendLine($"{name}: m {metres} eye {eye.x:F2},{eye.y:F2},{eye.z:F2} fwd {fwd.x:F2},{fwd.z:F2} far {cam.farClipPlane:F0}"
                                      + Probe(eye, fwd));
                }
                air.viewCamera = null;
            }
            finally
            {
                foreach (var o in temp) if (o) UnityEngine.Object.DestroyImmediate(o);
                if (wasActive.IsValid()) SceneManager.SetActiveScene(wasActive);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            return report.ToString();
        }

        /// <summary>What is around the eye: roof height, walls left/right, what is straight ahead.</summary>
        static string Probe(Vector3 eye, Vector3 fwd)
        {
            string Hit(Vector3 dir, float reach)
            {
                if (!Physics.Raycast(eye, dir, out var h, reach, ~0, QueryTriggerInteraction.Ignore)) return "-";
                var r = h.collider.GetComponent<Renderer>();
                return $"{h.distance:F1}m {(r && r.enabled ? "" : "(invisible) ")}{h.collider.name}";
            }
            var right = Vector3.Cross(Vector3.up, fwd);
            return $"\n   up {Hit(Vector3.up, 30)} | ahead {Hit(fwd, 60)} | left {Hit(-right, 20)} | right {Hit(right, 20)}";
        }

        static Vector3 RoutePoint(GodMountainsAtmosphere air, float metres)
        {
            var route = air.route;
            float walked = 0;
            for (int i = 0; i + 1 < route.Length; i++)
            {
                float length = Vector3.Distance(route[i].position, route[i + 1].position);
                if (walked + length >= metres) return Vector3.Lerp(route[i].position, route[i + 1].position, (metres - walked) / Mathf.Max(length, 1e-3f));
                walked += length;
            }
            return route[route.Length - 1].position;
        }

        static Vector3 RouteForward(GodMountainsAtmosphere air, float metres)
        {
            var d = RoutePoint(air, metres + 1) - RoutePoint(air, metres - 1);
            d.y = 0;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        }
    }
}
