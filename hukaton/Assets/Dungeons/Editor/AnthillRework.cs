// One-off authoring (temporary, removed with the other authoring tools before delivery): the walk-in entrance hill
// prefab and the kit's module variants and backstop.
using System.Collections.Generic;
using System.Linq;
using Hakaton.Dungeons.Unity;
using Hakaton.WorldAssembly.Unity;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Dungeons.EditorTools
{
    static class AnthillRework
    {
        const string Art = "Assets/AnthillArt/Prefabs";
        const string KitPath = "Assets/Dungeons/Anthill/Anthill Kit.asset";
        const string EntrancePath = "Assets/Dungeons/Anthill/Anthill Entrance.prefab";

        static GameObject P(string group, string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Art}/{group}/{name}.prefab");

        static GameObject[] Variants(string group, string name)
        {
            var list = new List<GameObject>();
            foreach (var suffix in new[] { "", "_B", "_C", "_D" })
            {
                var p = P(group, name + suffix);
                if (p) list.Add(p);
            }
            return list.ToArray();
        }

        static string Kit(string arg)
        {
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            kit.walls = Variants("Shell", "AN01_EarthWall");
            kit.corners = Variants("Shell", "AN02_EarthCorner");
            kit.vaults = Variants("Shell", "AN03_CeilingVault");
            kit.floors = Variants("Shell", "AN06_EarthFloor");
            if (kit.corners.Length > 0) kit.corner = kit.corners[0];
            if (kit.vaults.Length > 0) kit.vault = kit.vaults[0];
            kit.backstop = AssetDatabase.LoadAssetAtPath<Material>("Assets/AnthillArt/Materials/M_Ant_Earth.mat");
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return $"walls {kit.walls.Length} corners {kit.corners.Length} vaults {kit.vaults.Length} floors {kit.floors.Length} backstop {(kit.backstop ? kit.backstop.name : "none")}";
        }

        static string Entrance(string arg)
        {
            var root = PrefabUtility.LoadPrefabContents(EntrancePath);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var hillPrefab = P("Entrance", "AN09_EntranceHill");
                if (hillPrefab)
                {
                    var hill = (GameObject)PrefabUtility.InstantiatePrefab(hillPrefab, root.transform);
                    hill.name = "Hill";
                    hill.transform.localPosition = Vector3.zero;
                    hill.transform.localRotation = Quaternion.identity;
                }
                var passage = new GameObject("Passage").transform;
                passage.SetParent(root.transform, false);
                var outside = new GameObject("Outside").transform;
                outside.SetParent(root.transform, false);
                outside.localPosition = new Vector3(0, 0, -6.5f);
                var entrance = root.GetComponent<DungeonEntrance>();
                entrance.passage = passage;
                entrance.outside = outside;
                entrance.clearCentre = new Vector3(0, 0, 5f);
                entrance.clearRadius = 13f;
                PrefabUtility.SaveAsPrefabAsset(root, EntrancePath);
                return "entrance: hill " + (hillPrefab ? hillPrefab.name : "none yet");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
