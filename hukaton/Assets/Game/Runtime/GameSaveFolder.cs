using System;
using System.IO;
using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>
    /// The one place that says which folder the game's save lives in. Normally it is the player's own folder
    /// (<see cref="PlayerFolder"/>, «Application.persistentDataPath/Hakaton»). Tests redirect it to a fresh temporary
    /// folder for the duration of a test (<see cref="UseTemporaryForTests"/> … <see cref="EndTemporaryForTests"/>), so
    /// the player's files are never read, moved or written while a test runs, and an aborted run leaves them untouched.
    /// <para>
    /// Lifetime of the redirect, in the Editor: it is kept in SessionState (survives a domain reload in the middle of a
    /// run) and is cleared at the start of every Play (so a normal Play never inherits a redirect left by an aborted
    /// test) and once the Editor is back in Edit mode (after the test scene has been torn down — its last autosave still
    /// lands in the temporary folder). In a build nothing redirects it.
    /// </para>
    /// </summary>
    public static class GameSaveFolder
    {
        /// <summary>The player's real save folder.</summary>
        public static string PlayerFolder => Path.Combine(Application.persistentDataPath, "Hakaton");

        /// <summary>Where the game reads and writes its save right now.</summary>
        public static string Current => Redirect ?? PlayerFolder;

        /// <summary>A test has pointed the save somewhere else.</summary>
        public static bool IsRedirected => Redirect != null;

        /// <summary>Parent of every test folder: the project's own Temp (wiped when the Editor closes), or the cache in a player.</summary>
        public static string TemporaryRoot
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(Directory.GetCurrentDirectory(), "Temp", "HakatonTestSaves");
#else
                return Path.Combine(Application.temporaryCachePath, "HakatonTestSaves");
#endif
            }
        }

        /// <summary>
        /// Tests: from now on the save lives in a new empty folder of its own under <see cref="TemporaryRoot"/>
        /// (folders left by an earlier, aborted test are removed first). Returns that folder.
        /// </summary>
        public static string UseTemporaryForTests(string label = "test")
        {
            string root = TemporaryRoot;
            DeleteLeftovers(root);
            string folder = Path.Combine(root, Sanitize(label) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            Redirect = folder;
            return folder;
        }

        /// <summary>Tests: the save goes back to the player's folder; the temporary folder and its files are deleted.</summary>
        public static void EndTemporaryForTests()
        {
            string folder = Redirect;
            Redirect = null;
            if (folder != null && IsInside(folder, TemporaryRoot) && Directory.Exists(folder))
            {
                try { Directory.Delete(folder, true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        static void DeleteLeftovers(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (string folder in Directory.GetDirectories(root))
            {
                try { Directory.Delete(folder, true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        static bool IsInside(string path, string root)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(parent, StringComparison.Ordinal) && full.Length > parent.Length;
        }

        static string Sanitize(string label)
        {
            if (string.IsNullOrEmpty(label)) return "test";
            var chars = label.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }

        /// <summary>Every Play starts with the player's folder: a redirect is only ever set by a running test.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnPlay() => Redirect = null;

#if UNITY_EDITOR
        const string RedirectKey = "Hakaton.TestSaveFolder";

        static string Redirect
        {
            get { string folder = UnityEditor.SessionState.GetString(RedirectKey, ""); return folder.Length > 0 ? folder : null; }
            set { if (value == null) UnityEditor.SessionState.EraseString(RedirectKey); else UnityEditor.SessionState.SetString(RedirectKey, value); }
        }

        [UnityEditor.InitializeOnLoadMethod]
        static void ClearWhenPlayEnds()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.EnteredEditMode && Redirect != null) EndTemporaryForTests();
        }
#else
        static string Redirect { get; set; }
#endif
    }
}
