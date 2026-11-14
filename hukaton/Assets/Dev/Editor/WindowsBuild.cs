using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hakaton.Dev
{
    /// <summary>
    /// Windows build of the game (menu Hakaton/Build/Windows, or the bridge: invoke Hakaton.Dev.WindowsBuild.Build):
    /// the scenes of Build Settings into Builds/Windows next to Assets, 64-bit, Hakaton.exe. Copy the whole folder
    /// to the Windows machine and run Hakaton.exe.
    /// </summary>
    static class WindowsBuild
    {
        [MenuItem("Hakaton/Build/Windows")]
        static void Menu() => Debug.Log("[Build] " + Build(""));

        public static string Build(string _)
        {
            TextureCompression.Apply("");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                return "FAILED the Windows build module is not installed for this Unity version";
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.Combine(project, "Builds", "Windows");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(output, "Hakaton.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                var errors = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(12);
                return $"FAILED {summary.result}, {summary.totalErrors} errors\n" + string.Join("\n", errors);
            }
            // Debug symbols are not needed to play.
            foreach (var folder in Directory.GetDirectories(output, "*DoNotShip"))
                Directory.Delete(folder, true);
            long bytes = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            return $"windows build in {summary.totalTime.TotalMinutes:0.0} min, {bytes / 1048576f:0.0} MB → {output}";
        }
    }
}
