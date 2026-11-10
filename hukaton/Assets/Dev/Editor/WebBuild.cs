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
    /// Textures are capped for the browser by the editor's own import override (Build Settings → Asset Import
    /// Overrides), not by the project's import settings: the full-size atlases are over 2 GB, more than a browser tab
    /// holds. The project's .meta files stay as authored; the cap is lifted again after the build.
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
            int cap = EditorUserBuildSettings.overrideMaxTextureSize;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            EditorUserBuildSettings.overrideMaxTextureSize = WebTextureCap;
            BuildReport report;
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
                EditorUserBuildSettings.overrideMaxTextureSize = cap;
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
                    .Replace("// config.autoSyncPersistentDataPath = true;", "config.autoSyncPersistentDataPath = true;");
                File.WriteAllText(page, html);
            }
            if (File.Exists(style)) File.AppendAllText(style, "\nbody { background: #231F20; overflow: hidden }\n");
        }

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
