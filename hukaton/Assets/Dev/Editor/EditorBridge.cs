using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Hakaton.Dev
{
    /// <summary>
    /// Development-only bridge between the Editor and external tooling.
    /// Mirrors the Console and compiler output to <c>Logs/</c> (git-ignored) and executes
    /// simple commands dropped into <c>Logs/bridge/inbox/*.cmd</c>:
    /// <c>refresh</c>, <c>play</c>, <c>stop</c>, <c>pause</c>, <c>open &lt;scene&gt;</c>,
    /// <c>tests EditMode|PlayMode [filter]</c>, <c>shot &lt;name&gt; [superSize]</c>,
    /// <c>invoke &lt;Type.Method&gt; [arg]</c>, <c>timescale &lt;x&gt;</c>.
    /// Nothing here ships with the game: the assembly is Editor-only.
    /// </summary>
    [InitializeOnLoad]
    static class EditorBridge
    {
        static readonly string Root = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "bridge");
        static readonly string Inbox = Path.Combine(Root, "inbox");
        static readonly string ConsoleLog = Path.Combine(Root, "console.log");
        static readonly string CompileLog = Path.Combine(Root, "compile.log");
        static readonly string StateFile = Path.Combine(Root, "state.txt");
        static readonly object Gate = new object();
        static double nextPoll;
        static readonly Dictionary<string, List<CompilerMessage>> CompileMessages = new Dictionary<string, List<CompilerMessage>>();

        static EditorBridge()
        {
            Directory.CreateDirectory(Inbox);
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            WriteState("domain-reloaded");
        }

        // ---------- mirrors ----------

        static void OnLog(string message, string stackTrace, LogType type)
        {
            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" [").Append(type).Append("] ").AppendLine(message);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                foreach (var line in stackTrace.Split('\n').Take(8)) if (line.Length > 0) builder.Append("    ").AppendLine(line);
            Append(ConsoleLog, builder.ToString());
        }

        static void OnCompilationStarted(object context)
        {
            lock (Gate) CompileMessages.Clear();
            File.WriteAllText(CompileLog, $"STARTED {DateTime.Now:HH:mm:ss}\n");
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            lock (Gate) CompileMessages[Path.GetFileNameWithoutExtension(assembly)] = messages.ToList();
        }

        static void OnCompilationFinished(object context)
        {
            var builder = new StringBuilder();
            int errors = 0, warnings = 0;
            lock (Gate)
                foreach (var pair in CompileMessages)
                    foreach (var message in pair.Value)
                    {
                        if (message.type == CompilerMessageType.Error) errors++; else warnings++;
                        builder.Append(message.type == CompilerMessageType.Error ? "ERROR " : "warn  ")
                            .Append(pair.Key).Append(": ").AppendLine(message.message);
                    }
            File.WriteAllText(CompileLog, $"FINISHED {DateTime.Now:HH:mm:ss} errors={errors} warnings={warnings}\n{builder}");
        }

        static void OnPlayMode(PlayModeStateChange change) => WriteState(change.ToString());

        static void WriteState(string evt)
        {
            try
            {
                File.WriteAllText(StateFile,
                    $"{DateTime.Now:HH:mm:ss} {evt}\nplaying={EditorApplication.isPlaying} paused={EditorApplication.isPaused} " +
                    $"compiling={EditorApplication.isCompiling} scene={EditorSceneManager.GetActiveScene().path}\n");
            }
            catch (IOException) { }
        }

        static void Append(string path, string text)
        {
            lock (Gate)
            {
                try { File.AppendAllText(path, text); }
                catch (IOException) { }
            }
        }

        // ---------- commands ----------

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            if (!Directory.Exists(Inbox)) return;
            foreach (var file in Directory.GetFiles(Inbox, "*.cmd").OrderBy(f => f))
            {
                string text;
                try { text = File.ReadAllText(file).Trim(); File.Delete(file); }
                catch (IOException) { continue; }
                string id = Path.GetFileNameWithoutExtension(file);
                string result;
                try { result = Execute(id, text); }
                catch (Exception exception) { result = "FAILED " + exception; }
                File.WriteAllText(Path.Combine(Root, id + ".out"), result + "\n");
            }
        }

        static string Execute(string id, string command)
        {
            var parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string argument = parts.Length > 1 ? parts[1].Trim() : "";
            switch (verb)
            {
                case "refresh":
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    return "OK refresh";
                case "play":
                    EditorApplication.isPlaying = true;
                    return "OK play requested";
                case "stop":
                    EditorApplication.isPlaying = false;
                    return "OK stop requested";
                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return "OK paused=" + EditorApplication.isPaused;
                case "open":
                    if (EditorApplication.isPlaying) return "FAILED cannot open scenes while playing";
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                    EditorSceneManager.OpenScene(argument);
                    return "OK opened " + argument;
                case "shot":
                    return Screenshot(argument);
                case "timescale":
                    Time.timeScale = float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture);
                    return "OK timescale=" + Time.timeScale;
                case "invoke":
                    return Invoke(argument);
                case "tests":
                    return RunTests(id, argument);
                case "state":
                    WriteState("query");
                    return File.ReadAllText(StateFile);
                default:
                    return "FAILED unknown command: " + command;
            }
        }

        static string Screenshot(string argument)
        {
            var parts = argument.Split(' ');
            string name = string.IsNullOrEmpty(parts[0]) ? DateTime.Now.ToString("HHmmss") : parts[0];
            int superSize = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            string path = Path.Combine(Root, "shots", name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path, superSize);
            return "OK capturing " + path + " (written at end of frame; Game View must be visible/playing)";
        }

        static string Invoke(string argument)
        {
            var parts = argument.Split(new[] { ' ' }, 2);
            int dot = parts[0].LastIndexOf('.');
            string typeName = parts[0].Substring(0, dot), methodName = parts[0].Substring(dot + 1);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
            if (type == null) return "FAILED type not found: " + typeName;
            var method = type.GetMethod(methodName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (method == null) return "FAILED method not found: " + methodName;
            object[] args = method.GetParameters().Length == 0 ? null : new object[] { parts.Length > 1 ? parts[1] : "" };
            return "OK " + method.Invoke(null, args);
        }

        static string RunTests(string id, string argument)
        {
            var parts = argument.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var mode = parts.Length > 0 && parts[0].Equals("PlayMode", StringComparison.OrdinalIgnoreCase) ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter { testMode = mode };
            if (parts.Length > 1) filter.groupNames = new[] { parts[1] };
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new TestReporter(Path.Combine(Root, id + ".tests.txt")));
            api.Execute(new ExecutionSettings(filter));
            return $"OK tests started mode={mode} filter={(parts.Length > 1 ? parts[1] : "*")}";
        }

        sealed class TestReporter : ICallbacks
        {
            readonly string path;
            readonly StringBuilder failures = new StringBuilder();
            int passed, failed, skipped;
            public TestReporter(string path) { this.path = path; File.WriteAllText(path, "RUNNING\n"); }
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                switch (result.TestStatus)
                {
                    case TestStatus.Passed: passed++; break;
                    case TestStatus.Failed:
                        failed++;
                        failures.AppendLine("FAIL " + result.FullName).AppendLine("    " + result.Message?.Trim())
                            .AppendLine("    " + string.Join("\n    ", (result.StackTrace ?? "").Split('\n').Take(6)));
                        break;
                    default: skipped++; break;
                }
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                File.WriteAllText(path, $"DONE passed={passed} failed={failed} skipped={skipped}\n{failures}");
            }
        }
    }
}
