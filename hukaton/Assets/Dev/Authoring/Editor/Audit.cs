using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Hakaton.Authoring
{
    /// <summary>Lists assets that nothing in the game scene (or Resources) depends on. Temporary.</summary>
    static class Audit
    {
        public static string Unused()
        {
            var roots = new List<string> { "Assets/Game/Scenes/Hakaton.unity" };
            roots.AddRange(AssetDatabase.FindAssets("", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Contains("/Resources/")));
            var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
            var unused = AssetDatabase.FindAssets("", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p) && !used.Contains(p))
                .Where(p => !p.EndsWith(".cs") && !p.EndsWith(".asmdef") && !p.StartsWith("Assets/Dev/"))
                .OrderBy(p => p).ToList();
            File.WriteAllLines("Logs/bridge/unused.txt", unused);
            File.WriteAllLines("Logs/bridge/used.txt", used.OrderBy(p => p));
            return $"used={used.Count} unused={unused.Count}";
        }
    }
}
