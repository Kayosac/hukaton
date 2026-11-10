using System;
using Hakaton.Expeditions;
using UnityEngine;
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif

namespace Hakaton.EnemyDebug
{
    [CreateAssetMenu(menuName = "Hakaton/Debug/Enemy catalogue")]
    public sealed class EnemyDebugCatalogue : ScriptableObject
    {
        [Tooltip("Search all game prefab folders. Only prefab roots with WorldCreature are enemies; nested level instances are excluded.")]
        public string[] searchFolders = { "Assets" };
        [Tooltip("Snapshot for a development build. The Editor discovers current prefabs on every scene start.")]
        public WorldCreature[] buildSnapshot = Array.Empty<WorldCreature>();

        public WorldCreature[] Discover()
        {
#if UNITY_EDITOR
            return AssetDatabase.FindAssets("t:Prefab", searchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.StartsWith("Assets/EnemyDebug/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab)
                .Select(prefab => prefab.GetComponent<WorldCreature>())
                .Where(creature => creature && creature.view && creature.view.idle && creature.body && creature.navigator)
                .ToArray();
#else
            return buildSnapshot;
#endif
        }
    }
}
