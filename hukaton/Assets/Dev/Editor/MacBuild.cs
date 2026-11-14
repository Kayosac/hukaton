using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hakaton.Dev
{
    /// <summary>
    /// macOS build of the game (menu Hakaton/Build/macOS, or the bridge: invoke Hakaton.Dev.MacBuild.Build):
    /// the scenes of Build Settings into Builds/macOS/PENUS.app next to Assets, one Universal app for Apple silicon
    /// and Intel Macs, plus Builds/Hakaton-macOS.zip packed with ditto so the app keeps its executable bit and
    /// signature. On another Mac: unzip, then the first launch goes through Right click → Open (or System Settings →
    /// Privacy &amp; Security → Open Anyway), because the app is not notarized.
    /// </summary>
    static class MacBuild
    {
        [MenuItem("Hakaton/Build/macOS")]
        static void Menu() => Debug.Log("[Build] " + Build(""));

        public static string Build(string _)
        {
            TextureCompression.Apply("");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                return "FAILED the macOS build module is not installed for this Unity version";
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.Combine(project, "Builds", "macOS");
            string zip = Path.Combine(project, "Builds", "Hakaton-macOS.zip");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            if (File.Exists(zip)) File.Delete(zip);
            Directory.CreateDirectory(output);
            string app = Path.Combine(output, "PENUS.app");

            // Universal binary; the user's own setting is put back after the build.
            object previous = SwapArchitecture("x64ARM64");
            string architecture = ArchitectureName();
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = app,
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                if (previous != null) SwapArchitecture(previous.ToString());
            }
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

            var ditto = Process.Start(new ProcessStartInfo("/usr/bin/ditto", $"-c -k --sequesterRsrc --keepParent \"{app}\" \"{zip}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            string dittoErrors = ditto.StandardError.ReadToEnd();
            ditto.WaitForExit();
            string packed = ditto.ExitCode == 0 && File.Exists(zip)
                ? $"zip {new FileInfo(zip).Length / 1048576f:0.0} MB → {zip}"
                : $"zip FAILED (ditto exit {ditto.ExitCode}) {dittoErrors}";
            return $"macOS build ({architecture}) in {summary.totalTime.TotalMinutes:0.0} min, {bytes / 1048576f:0.0} MB → {app}; {packed}";
        }

        // UnityEditor.OSXStandalone.UserBuildSettings.architecture lives in the macOS platform extension,
        // so it is reached by name: the editor compiles even where that module is missing.
        static System.Reflection.PropertyInfo ArchitectureProperty()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false))
                .FirstOrDefault(t => t != null);
            return type?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        }

        static object SwapArchitecture(string value)
        {
            var property = ArchitectureProperty();
            if (property == null || !property.PropertyType.IsEnum) return null;
            object previous = property.GetValue(null);
            property.SetValue(null, Enum.Parse(property.PropertyType, value));
            return previous;
        }

        static string ArchitectureName() => ArchitectureProperty()?.GetValue(null)?.ToString() ?? "architecture unknown";
    }
}
