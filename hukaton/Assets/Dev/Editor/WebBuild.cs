using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hakaton.Dev
{
    /// <summary>
    /// Web build of the game (menu Hakaton/Build/Web, or the bridge: invoke Hakaton.Dev.WebBuild.Build): the scenes of
    /// Build Settings into Builds/Web next to Assets, gzip with decompression fallback so any static server can host
    /// it, and next to it the launcher from WebLauncher~ — double-click start.command (macOS) or start.bat (Windows):
    /// a small local server on the folder opens the game in the browser.
    /// Textures are capped for the browser by a temporary WebGL override on every texture bigger than the cap: the
    /// full-size atlases are over 2 GB, more than a browser tab holds. A texture cut by hand into Sprites kept in a
    /// separate asset (the stitched-linen button skin) is left full size: those sprites store their rects in the
    /// texture's own pixels and fall apart on a smaller texture. The .meta files are restored byte for byte after
    /// the build.
    /// </summary>
    static class WebBuild
    {
        [MenuItem("Hakaton/Build/Web")]
        static void Menu() => Debug.Log("[Build] " + Build(""));

        [MenuItem("Hakaton/Build/Web and play")]
        static void MenuAndPlay()
        {
            string result = Build("");
            Debug.Log("[Build] " + result);
            if (result.StartsWith("web build")) Play("");
        }

        [MenuItem("Hakaton/Build/Play the web build")]
        static void MenuPlay() => Debug.Log("[Build] " + Play(""));

        /// <summary>Starts the launcher of the last web build: its server in a terminal window, the game in the browser.</summary>
        public static string Play(string _)
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Web"));
            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            string launcher = Path.Combine(output, windows ? "start.bat" : "start.command");
            if (!File.Exists(launcher)) return "FAILED no web build yet: Hakaton/Build/Web";
            if (windows) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher) { UseShellExecute = true, WorkingDirectory = output });
            else System.Diagnostics.Process.Start("/usr/bin/open", $"\"{launcher}\"");
            return "started " + launcher;
        }

        /// <summary>Largest texture side in the web build, px.</summary>
        const int WebTextureCap = 1024;

        public static string Build(string _)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                return "FAILED the Web (WebGL) build module is not installed for this Unity version";
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.Combine(project, "Builds", "Web");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            // Gzip with the loader's own fallback: served right by any server, natively decompressed by ours.
            var compression = PlayerSettings.WebGL.compressionFormat;
            bool fallback = PlayerSettings.WebGL.decompressionFallback;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            BuildReport report;
            var capped = CapTextures();
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                RestoreTextures(capped);
            }
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                var errors = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(12);
                return $"FAILED {summary.result}, {summary.totalErrors} errors\n" + string.Join("\n", errors);
            }

            File.WriteAllText(Path.Combine(project, "Builds", "Web-report.txt"), Report(report));
            FitPage(Path.Combine(output, "index.html"), Path.Combine(output, "TemplateData", "style.css"));

            // The launcher.
            string launcher = Path.Combine(Application.dataPath, "Dev", "Editor", "WebLauncher~");
            foreach (var file in Directory.GetFiles(launcher)) File.Copy(file, Path.Combine(output, Path.GetFileName(file)), true);
            // A double-clickable script must be executable.
            if (Application.platform != RuntimePlatform.WindowsEditor)
                using (var chmod = System.Diagnostics.Process.Start("/bin/chmod", $"+x \"{Path.Combine(output, "start.command")}\" \"{Path.Combine(output, "serve.py")}\""))
                    chmod?.WaitForExit();
            long bytes = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            return $"web build in {summary.totalTime.TotalMinutes:0.0} min, {bytes / 1048576f:0.0} MB → {output}";
        }

        /// <summary>Puts a temporary WebGL max size on every texture bigger than the cap; returns their original .meta texts.</summary>
        static Dictionary<string, string> CapTextures()
        {
            // Textures behind hand-made Sprites stored in other assets keep their full size.
            var keep = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter) continue;
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                    if (sprite.texture) keep.Add(AssetDatabase.GetAssetPath(sprite.texture));
            }
            var metas = new Dictionary<string, string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (keep.Contains(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                    importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                    var web = importer.GetPlatformTextureSettings("WebGL");
                    var main = importer.GetDefaultPlatformTextureSettings();
                    int size = Mathf.Min(Mathf.Max(width, height), web.overridden ? web.maxTextureSize : main.maxTextureSize);
                    if (size <= WebTextureCap) continue;
                    metas[path] = File.ReadAllText(path + ".meta");
                    if (!web.overridden)
                    {
                        web.format = TextureImporterFormat.Automatic;
                        web.textureCompression = main.textureCompression;
                        web.crunchedCompression = main.crunchedCompression;
                        web.compressionQuality = main.compressionQuality;
                        web.overridden = true;
                    }
                    web.maxTextureSize = WebTextureCap;
                    importer.SetPlatformTextureSettings(web);
                    importer.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"[Build] web cap {WebTextureCap} on {metas.Count} textures, kept full size: {string.Join(", ", keep)}");
            return metas;
        }

        static void RestoreTextures(Dictionary<string, string> metas)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var pair in metas)
                {
                    File.WriteAllText(pair.Key + ".meta", pair.Value);
                    AssetDatabase.ImportAsset(pair.Key, ImportAssetOptions.ForceUpdate);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        /// <summary>
        /// The default page shows the game in a 960 × 600 box: make it fill the browser window on dark ground, call it
        /// Hakaton, and keep saves between visits (persistent data synced to the browser's storage).
        /// </summary>
        static void FitPage(string page, string style)
        {
            if (File.Exists(page))
            {
                string html = File.ReadAllText(page)
                    .Replace("<title>Unity Web Player | " + PlayerSettings.productName + "</title>", "<title>Hakaton</title>")
                    .Replace("canvas.style.width = \"960px\";\n        canvas.style.height = \"600px\";",
                        "// Hakaton: the game fills the browser window.\n        document.querySelector(\"#unity-container\").className = \"unity-mobile\";\n        canvas.className = \"unity-mobile\";")
                    .Replace("// config.autoSyncPersistentDataPath = true;", "config.autoSyncPersistentDataPath = true;")
                    .Replace("</canvas>", "</canvas>\n      <button id=\"hakaton-fullscreen\" type=\"button\">На весь экран</button>")
                    .Replace("document.querySelector(\"#unity-loading-bar\").style.display = \"none\";",
                        "document.querySelector(\"#unity-loading-bar\").style.display = \"none\";\n" + FullscreenScript);
                File.WriteAllText(page, html);
            }
            if (File.Exists(style)) File.AppendAllText(style, "\nbody { background: #231F20; overflow: hidden }\n" + FullscreenStyle);
        }

        /// <summary>
        /// The fullscreen button over the game: fullscreen keeps the cursor inside the game, and in Chrome the keyboard
        /// lock hands Escape to the game instead of dropping fullscreen and the captured cursor (hold Escape to leave).
        /// </summary>
        const string FullscreenScript = @"                const fullscreen = document.querySelector(""#hakaton-fullscreen"");
                fullscreen.style.display = ""block"";
                fullscreen.onclick = () => {
                  unityInstance.SetFullscreen(1);
                  if (navigator.keyboard && navigator.keyboard.lock) navigator.keyboard.lock([""Escape""]).catch(() => {});
                  canvas.focus();
                };
                document.addEventListener(""fullscreenchange"", () => {
                  fullscreen.style.display = document.fullscreenElement ? ""none"" : ""block"";
                  if (!document.fullscreenElement && navigator.keyboard && navigator.keyboard.unlock) navigator.keyboard.unlock();
                });";

        const string FullscreenStyle = @"#hakaton-fullscreen { display: none; position: fixed; top: 12px; right: 12px; z-index: 10;
  padding: 8px 14px; border: 2px solid #E9DCC4; border-radius: 4px; background: rgba(35, 31, 32, .78);
  color: #E9DCC4; font: 15px/1 sans-serif; cursor: pointer; opacity: .75 }
#hakaton-fullscreen:hover { opacity: 1 }
";

        /// <summary>What the last build is made of (from Library/LastBuild.buildreport): bytes by folder and the biggest assets.</summary>
        public static string LastReport(string _)
        {
            var objects = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget("Library/LastBuild.buildreport");
            var report = objects.OfType<BuildReport>().FirstOrDefault();
            return report ? Report(report) : "FAILED no build report";
        }

        static string Report(BuildReport report)
        {
            var assets = report.packedAssets.SelectMany(p => p.contents)
                .GroupBy(c => string.IsNullOrEmpty(c.sourceAssetPath) ? "(built-in)" : c.sourceAssetPath)
                .Select(g => (path: g.Key, bytes: g.Sum(c => (long)c.packedSize), type: g.First().type?.Name))
                .OrderByDescending(a => a.bytes).ToList();
            string Folder(string path)
            {
                var parts = path.Split('/');
                return parts.Length > 2 ? parts[0] + "/" + parts[1] + "/" + (parts.Length > 3 ? parts[2] : "") : path;
            }
            var text = new System.Text.StringBuilder();
            text.AppendLine($"total {assets.Sum(a => a.bytes) / 1048576f:0.0} MB in {assets.Count} assets");
            text.AppendLine("by folder:");
            foreach (var g in assets.GroupBy(a => Folder(a.path)).Select(g => (g.Key, bytes: g.Sum(a => a.bytes))).OrderByDescending(g => g.bytes).Take(30))
                text.AppendLine($"  {g.bytes / 1048576f,8:0.0} MB  {g.Key}");
            text.AppendLine("by type:");
            foreach (var g in assets.GroupBy(a => a.type).Select(g => (g.Key, bytes: g.Sum(a => a.bytes))).OrderByDescending(g => g.bytes).Take(12))
                text.AppendLine($"  {g.bytes / 1048576f,8:0.0} MB  {g.Key}");
            text.AppendLine("biggest:");
            foreach (var a in assets.Take(60)) text.AppendLine($"  {a.bytes / 1048576f,8:0.00} MB  {a.type,-14} {a.path}");
            return text.ToString();
        }
    }
}
