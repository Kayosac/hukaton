using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.EditorTools
{
    /// <summary>Draws world plans from above into Logs/world-plans/plan-seed-N.png (menu Hakaton/World/Draw plans).</summary>
    public static class WorldPlanPreview
    {
        const int R = 24, P = 12;

        [MenuItem("Hakaton/World/Draw plans (seeds 1-3)")]
        static void Menu() => Debug.Log(Draw("1,2,3") + " → Logs/world-plans");

        /// <summary>Share of chunk kinds per ring over seeds "a-b": how much of the world is plain ground.</summary>
        public static string Stats(string seeds)
        {
            var parts = seeds.Split('-');
            int from = int.Parse(parts[0]), to = int.Parse(parts[parts.Length - 1]);
            var counts = new System.Collections.Generic.SortedDictionary<string, int>[3];
            var totals = new int[3];
            for (int r = 0; r < 3; r++) counts[r] = new System.Collections.Generic.SortedDictionary<string, int>();
            for (int seed = from; seed <= to; seed++)
            {
                var map = new WorldMap(seed);
                for (int x = -14; x <= 14; x++)
                    for (int y = -14; y <= 14; y++)
                    {
                        if (x * x + y * y > 14 * 14) continue;
                        var plan = map.Chunk(x, y);
                        string key = plan.Shape.Kind + (plan.Shape.Kind == ShapeKind.Plain || plan.Shape.Kind == ShapeKind.PlateauTop ? "/" + plan.Variant.Id : "");
                        counts[plan.Ring].TryGetValue(key, out int c);
                        counts[plan.Ring][key] = c + 1;
                        totals[plan.Ring]++;
                    }
            }
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < 3; r++)
            {
                sb.Append($"ring {r}:");
                foreach (var kv in counts[r]) sb.Append($" {kv.Key} {100f * kv.Value / totals[r]:0}%");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Draw(string seeds)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            foreach (var token in seeds.Split(','))
            {
                int seed = int.Parse(token.Trim());
                var map = new WorldMap(seed);
                int n = (2 * R + 1) * P;
                var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
                var px = new Color32[n * n];
                for (int x = -R; x <= R; x++)
                    for (int y = -R; y <= R; y++)
                        Chunk(map.Chunk(x, y), (x + R) * P, (y + R) * P, px, n);
                // Ring outlines.
                foreach (float radius in new[] { map.Rules.InnerRadius, map.Rules.MiddleRadius })
                    for (int a = 0; a < 2000; a++)
                    {
                        float t = a / 2000f * Mathf.PI * 2, r = radius / map.Rules.ChunkSize * P;
                        int ix = (int)(n / 2 + Mathf.Cos(t) * r), iy = (int)(n / 2 + Mathf.Sin(t) * r);
                        if (ix >= 0 && iy >= 0 && ix < n && iy < n) px[iy * n + ix] = new Color32(255, 255, 255, 255);
                    }
                tex.SetPixels32(px);
                File.WriteAllBytes(Path.Combine(dir, $"plan-seed-{seed}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            return "drawn " + seeds;
        }

        static void Chunk(ChunkPlan p, int ox, int oy, Color32[] px, int n)
        {
            Color32 Level(int level) => level switch
            {
                0 => new Color32(118, 128, 78, 255),
                1 => new Color32(150, 140, 120, 255),
                _ => new Color32(205, 200, 190, 255),
            };
            // Corner-blended terrain: each sub-pixel takes the nearest corner's level.
            for (int i = 0; i < P; i++)
                for (int j = 0; j < P; j++)
                {
                    int corner = j >= P / 2 ? (i < P / 2 ? 0 : 1) : (i < P / 2 ? 3 : 2);
                    px[(oy + j) * n + ox + i] = Level(p.Corners[corner]);
                }
            void Fill(int x0, int y0, int x1, int y1, Color32 c)
            {
                for (int i = x0; i < x1; i++) for (int j = y0; j < y1; j++) px[(oy + j) * n + ox + i] = c;
            }
            Color32 water = new Color32(60, 110, 200, 255), canyon = new Color32(70, 45, 30, 255), hedge = new Color32(40, 70, 30, 255),
                corridor = new Color32(150, 160, 100, 255), mark = new Color32(250, 210, 60, 255), red = new Color32(220, 40, 40, 255);
            int m = P / 2, w = 2;
            if (p.Shape.Kind == ShapeKind.Maze)
            {
                Fill(0, 0, P, P, hedge);
                Fill(m - w, m - w, m + w, m + w, corridor);
                for (int s = 0; s < 4; s++)
                {
                    if (p.Mids[s] != Mid.MazeOpen) continue;
                    if (s == 0) Fill(m - w, m, m + w, P, corridor);
                    if (s == 1) Fill(m, m - w, P, m + w, corridor);
                    if (s == 2) Fill(m - w, 0, m + w, m, corridor);
                    if (s == 3) Fill(0, m - w, m, m + w, corridor);
                }
                if (p.MazeHeart) Fill(m - 1, m - 1, m + 1, m + 1, mark);
            }
            for (int s = 0; s < 4; s++)
            {
                if (p.Mids[s] != Mid.River && p.Mids[s] != Mid.Canyon) continue;
                var c = p.Mids[s] == Mid.River ? water : canyon;
                if (s == 0) Fill(m - 1, m - 1, m + 1, P, c);
                if (s == 1) Fill(m - 1, m - 1, P, m + 1, c);
                if (s == 2) Fill(m - 1, 0, m + 1, m + 1, c);
                if (s == 3) Fill(0, m - 1, m + 1, m + 1, c);
            }
            if (p.Shape.Id == "river-end") Fill(m - 3, m - 3, m + 3, m + 3, water);
            string v = p.Variant.Id;
            if (v == "bridge" || v == "ford" || v == "log" || v == "descent" || v == "ramp") Fill(m - 1, m - 1, m + 1, m + 1, mark);
            if (v == "island") Fill(m - 1, m - 1, m + 1, m + 1, mark);
            if (p.Shape.Kind == ShapeKind.Landmark)
            {
                var (kind, _, _) = Landmarks.Parse(p.Shape.Id);
                Fill(1, 1, P - 1, P - 1, kind == Landmarks.StartHill ? red : new Color32(200, 120, 220, 255));
            }
            if (p.Creature != null) px[(oy + 1) * n + ox + 1] = red;
        }
    }
}
