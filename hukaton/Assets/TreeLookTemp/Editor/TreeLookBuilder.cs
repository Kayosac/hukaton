using System.Text;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// TEMPORARY authoring script: builds the Sinister-tree look of the awakened tree. Deleted after use.
static class TreeLookBuilder
{
    const string Folder = "Assets/Game/Prefabs/Cards/TreeAwakening/";
    const string LookPath = Folder + "Awakened tree look (Sinister tree).prefab";
    const string AllyPath = Folder + "Awakened tree.prefab";
    const string EnemyPath = "Assets/Game/Prefabs/Sinister living tree.prefab";

    public static string Build()
    {
        var log = new StringBuilder();
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
        var enemyView = enemy.GetComponentInChildren<CreatureSpriteView>(true);
        var root = new GameObject("Awakened tree look (Sinister tree)");
        var look = root.AddComponent<SpriteTreeAllyLook>();
        var drawing = Object.Instantiate(enemyView.gameObject, root.transform);
        drawing.name = "Drawing";
        drawing.transform.localPosition = Vector3.zero;
        drawing.transform.localRotation = Quaternion.identity;
        drawing.transform.localScale = Vector3.one;
        look.view = drawing.GetComponent<CreatureSpriteView>();
        look.view.spriteRenderer = drawing.GetComponent<SpriteRenderer>();
        var lookAsset = PrefabUtility.SaveAsPrefabAsset(root, LookPath, out bool saved);
        Object.DestroyImmediate(root);
        log.Append("look saved=").Append(saved).Append(' ');

        var allyRoot = PrefabUtility.LoadPrefabContents(AllyPath);
        try
        {
            var ally = allyRoot.GetComponent<TreeAlly>();
            var old = ally.look ? ally.look.gameObject : null;
            int index = old ? old.transform.GetSiblingIndex() : 0;
            if (old) Object.DestroyImmediate(old);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(lookAsset, allyRoot.transform);
            instance.transform.SetSiblingIndex(index);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            ally.look = instance.GetComponent<SpriteTreeAllyLook>();
            var capsule = allyRoot.GetComponent<CapsuleCollider>();
            capsule.radius = .5f; capsule.height = 2.8f; capsule.center = new Vector3(0, 1.4f, 0);
            var agent = allyRoot.GetComponent<NavMeshAgent>();
            agent.radius = .5f; agent.height = 2.8f;
            ally.overhead.transform.localPosition = new Vector3(0, 3.85f, 0);
            ally.leafColour = new Color(.62f, .5f, .24f);
            PrefabUtility.SaveAsPrefabAsset(allyRoot, AllyPath, out bool allySaved);
            log.Append("ally saved=").Append(allySaved).Append(' ');
        }
        finally { PrefabUtility.UnloadPrefabContents(allyRoot); }

        log.Append("del prefab=").Append(AssetDatabase.DeleteAsset(Folder + "Awakened tree look (placeholder).prefab")).Append(' ');
        log.Append("del meshes=").Append(AssetDatabase.DeleteAsset(Folder + "Meshes")).Append(' ');
        AssetDatabase.SaveAssets();
        return log.Append(Verify()).ToString();
    }

    public static string Verify()
    {
        var ally = AssetDatabase.LoadAssetAtPath<GameObject>(AllyPath).GetComponent<TreeAlly>();
        var look = ally.look as SpriteTreeAllyLook;
        var b = new StringBuilder("verify: ");
        b.Append("look=").Append(look ? look.name : "null");
        if (look && look.view) b.Append(" idle=").Append(look.view.idle ? look.view.idle.name : "-")
            .Append(" back=").Append(look.view.idleBack ? look.view.idleBack.name : "-")
            .Append(" h=").Append(look.Height).Append(" mat=").Append(look.view.spriteRenderer.sharedMaterial.name);
        int missing = 0;
        foreach (var t in ally.GetComponentsInChildren<Transform>(true)) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        b.Append(" missingScripts=").Append(missing);
        b.Append(" children=");
        foreach (Transform c in ally.transform) b.Append(c.name).Append(';');
        return b.ToString();
    }
}
