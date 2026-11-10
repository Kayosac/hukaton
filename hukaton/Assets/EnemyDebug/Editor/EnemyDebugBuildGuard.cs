using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Hakaton.EnemyDebug.Editor
{
    // Catalog bookkeeping only: never generates or rebuilds scenes/prefabs.
    public sealed class EnemyDebugBuildGuard : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) == 0) return;
            foreach (var guid in AssetDatabase.FindAssets("t:EnemyDebugCatalogue", new[] { "Assets/EnemyDebug" }))
            {
                var catalogue = AssetDatabase.LoadAssetAtPath<EnemyDebugCatalogue>(AssetDatabase.GUIDToAssetPath(guid));
                catalogue.buildSnapshot = catalogue.Discover();
                EditorUtility.SetDirty(catalogue);
                AssetDatabase.SaveAssetIfDirty(catalogue);
            }
        }
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report != null && scene.path.StartsWith("Assets/EnemyDebug/")
                && (report.summary.options & BuildOptions.Development) == 0)
                throw new BuildFailedException("EnemyDebug is a development-only scene. Remove it from the release build scene list.");
        }
    }
}
