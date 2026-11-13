// TEMPORARY: which scene objects C01's swept volume (Logs/bridge/c01_sweep.txt) passes through, and how far the
// panorama peaks it touches must step back from the god to clear it.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Hakaton.GodMountains;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GodSweep
{
    struct S { public Vector3 c; public float r; public string tag; }

    static List<S> Spheres()
    {
        var list = new List<S>();
        foreach (var l in File.ReadAllLines("Logs/bridge/c01_sweep.txt"))
        {
            var p = l.Split(' ');
            float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            var a = new Vector3(F(2), F(3), F(4)); var b = new Vector3(F(5), F(6), F(7)); float r = F(8);
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / (r * .5f)));
            for (int i = 0; i <= n; i++) list.Add(new S { c = Vector3.Lerp(a, b, i / (float)n), r = r, tag = $"{p[0]} {p[1]} {p[9]}" });
        }
        return list;
    }

    static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1 / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>Deepest overlap (m) of the spheres with a mesh moved by `shift`, and the tag of the deepest.</summary>
    static (float depth, string tag) Overlap(Vector3[] w, int[] tri, Bounds bb, List<S> sph, Vector3 shift)
    {
        float worst = 0; string tag = "";
        foreach (var s in sph)
        {
            var c = s.c - shift;
            if (Vector3.Distance(bb.ClosestPoint(c), c) > s.r) continue;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = w[tri[i]], b = w[tri[i + 1]], d = w[tri[i + 2]];
                var mn = Vector3.Min(a, Vector3.Min(b, d)); var mx = Vector3.Max(a, Vector3.Max(b, d));
                if (c.x < mn.x - s.r || c.x > mx.x + s.r || c.y < mn.y - s.r || c.y > mx.y + s.r || c.z < mn.z - s.r || c.z > mx.z + s.r) continue;
                float dep = s.r - Vector3.Distance(Closest(c, a, b, d), c);
                if (dep > worst) { worst = dep; tag = s.tag; }
            }
        }
        return (worst, tag);
    }

    /// <summary>apply = "0": report; "1": move the touched panorama peaks back from the god and save the scene.</summary>
    public static string Run(string apply)
    {
        var enc = Object.FindFirstObjectByType<GodEncounter>();
        var god = enc.god.transform;
        var center = new Vector3(god.position.x, 0, god.position.z);
        var sph = Spheres();
        var all = new Bounds(sph[0].c, Vector3.zero);
        foreach (var s in sph) all.Encapsulate(new Bounds(s.c, Vector3.one * s.r * 2));
        var sb = new System.Text.StringBuilder();
        // panorama peaks: one group per "Вершина N"
        var groups = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
            .Where(f => f.sharedMesh && !f.transform.IsChildOf(god) && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().bounds.Intersects(all))
            .GroupBy(f => Group(f.transform));
        int moved = 0;
        foreach (var g in groups)
        {
            var parts = g.Select(f =>
            {
                var m = f.sharedMesh; var tr = f.transform;
                var w = m.vertices.Select(v => tr.TransformPoint(v)).ToArray();
                return (w, m.triangles, f.GetComponent<Renderer>().bounds);
            }).ToList();
            float worst = 0; string tag = "";
            foreach (var p in parts) { var o = Overlap(p.w, p.triangles, p.bounds, sph, Vector3.zero); if (o.depth > worst) { worst = o.depth; tag = o.tag; } }
            if (worst <= 0) continue;
            var root = g.Key;
            var radial = new Vector3(root.position.x, 0, root.position.z) - center; radial.y = 0; radial.Normalize();
            float step = 0; float left = worst;
            bool isPanorama = Path(root).Contains("Панорама");
            if (isPanorama)
            {
                for (step = 30; step < 2400; step += 30)
                {
                    float w2 = 0;
                    foreach (var p in parts) w2 = Mathf.Max(w2, Overlap(p.w, p.triangles, p.bounds, sph, radial * step).depth);
                    if (w2 <= 0) break;
                }
                step += 30;  // a margin
            }
            sb.AppendLine($"{Path(root)}: deepest {worst:F1} m at [{tag}] parts {parts.Count} " + (isPanorama ? $"-> back {step:F0} m along {radial:F2}" : "(level, left alone)"));
            if (apply == "1" && isPanorama)
            {
                Undo.RecordObject(root, "Panorama peak out of the god's reach");
                root.position += radial * step;
                moved++;
            }
        }
        if (apply == "1" && moved > 0)
        {
            EditorSceneManager.MarkSceneDirty(god.gameObject.scene);
            EditorSceneManager.SaveScene(god.gameObject.scene);
            sb.AppendLine($"moved {moved}, scene saved");
        }
        File.WriteAllText("Logs/bridge/god-sweep.txt", sb.ToString());
        return sb.Length > 0 ? sb.ToString().Split('\n').Length - 1 + " lines" : "clear";
    }

    static Transform Group(Transform t)
    {
        for (var x = t; x; x = x.parent)
            if (x.parent && x.parent.name == "Бескрайние вершины") return x;
        return t;
    }

    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
}
