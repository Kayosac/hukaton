using System.Linq;
using System.Text;
using Hakaton.Feel;
using Hakaton.SpecialItems;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-off (delete before delivery): the ant compass special item. Bridge: <c>invoke CompassSetup.Run</c>. Imports the
/// author's art (Art/Compass), makes the definition (800, after the first Queen), puts it in the catalogue, builds
/// Prefabs/Compass.prefab from the player's prefab (same hand, same hold) and adds it to «Special items».
/// </summary>
static class CompassSetup
{
    const string Root = "Assets/SpecialItems";

    public static string Run(string arg)
    {
        var log = new StringBuilder();
        var baseSprite = Import($"{Root}/Art/Compass/Compass base.png", $"{Root}/Art/Player/Player idle.png");
        // The ant at the dial's own pixel size: the base (1536 tall) is shown from a 512 import, so the 384 ant from 128.
        var antSprite = Import($"{Root}/Art/Compass/Compass ant.png", $"{Root}/Art/Player/Player idle.png", 128);
        var icon = Import($"{Root}/Art/Compass/CompassIcon.png", $"{Root}/Art/Player/PlayerIcon.png");
        if (!baseSprite || !antSprite || !icon) return "FAILED art";

        string defPath = $"{Root}/Data/Compass.asset";
        var item = AssetDatabase.LoadAssetAtPath<SpecialItemDefinition>(defPath);
        if (!item) { item = ScriptableObject.CreateInstance<SpecialItemDefinition>(); AssetDatabase.CreateAsset(item, defPath); }
        item.id = "ant_compass";
        item.displayName = "Компас-муравей";
        item.price = 800;
        item.unlock = SpecialItemUnlock.AfterQueen;
        item.use = SpecialItemUse.AlongsideCards;
        item.tableSprite = icon;
        item.menuIcon = icon;
        item.introduction = new[]
        {
            "Слышал, ты Матку завалил. Уважаю. А у меня тут кое-что с её стола.",
            "Компас. Только вместо стрелки — муравей. Живой. Не трогай его пальцем.",
            "Он всегда тянется к своим: наверху — к муравейнику, внизу — к их заначкам.",
        };
        item.boughtLine = "Корми его… хотя нет, не корми. Он сам найдёт.";
        item.howToUse = new[]
        {
            "На вылазке достаёшь — Q. Зажмёшь Q — выберешь, что достать.",
            "Наверху муравей тянет головой к ближайшему муравейнику.",
            "Внизу — к тайнику, который ещё не вскрыт. Вскрыл все — потянет к выходу.",
            "Не чует ничего — бегает по кругу. Значит, тут пусто.",
        };
        EditorUtility.SetDirty(item);

        var catalog = AssetDatabase.LoadAssetAtPath<SpecialItemCatalog>($"{Root}/Data/Special items.asset");
        if (!catalog.items.Contains(item)) catalog.items = catalog.items.Concat(new[] { item }).ToArray();
        EditorUtility.SetDirty(catalog);

        string prefabPath = $"{Root}/Prefabs/Compass.prefab";
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)) AssetDatabase.CopyAsset($"{Root}/Prefabs/Player.prefab", prefabPath);
        var contents = PrefabUtility.LoadPrefabContents(prefabPath);
        contents.name = "Compass";
        var tool = contents.GetComponentInChildren<CompassTool>(true);
        var player = contents.GetComponentInChildren<CassettePlayerTool>(true);
        if (player)
        {
            tool = player.gameObject.AddComponent<CompassTool>();
            tool.canvas = player.canvas;
            tool.hand = player.player;
            tool.body = player.body;
            tool.hold = player.hold;
            tool.enterSeconds = player.enterSeconds;
            tool.leaveSeconds = player.leaveSeconds;
            tool.drawSound = player.drawSound;
            foreach (var part in new Component[] { player.reelTop, player.reelBottom, player.lamp })
                if (part) Object.DestroyImmediate(part.gameObject);
            var music = player.music;
            Object.DestroyImmediate(player);
            if (music) Object.DestroyImmediate(music.gameObject);
        }
        tool.definition = item;
        tool.hand.name = "Compass in hand";
        tool.body.sprite = baseSprite;
        var antRect = tool.ant;
        if (!antRect)
        {
            var go = new GameObject("Ant", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            antRect = (RectTransform)go.transform;
            antRect.SetParent(tool.body.transform.parent, false);
            antRect.SetSiblingIndex(tool.body.transform.GetSiblingIndex() + 1);
        }
        var antImage = antRect.GetComponent<Image>();
        antImage.sprite = antSprite;
        antImage.material = tool.body.material;
        antImage.raycastTarget = false;
        // The author's layout at half size (base 1024 × 1536 shown 512 × 768): the ant layer's top-left (358, 335),
        // 384 px square, turned round its centre.
        antRect.anchorMin = antRect.anchorMax = new Vector2(0, 1);
        antRect.pivot = new Vector2(.5f, .5f);
        antRect.sizeDelta = new Vector2(192, 192);
        antRect.anchoredPosition = new Vector2((358 + 192) * .5f, -(335 + 192) * .5f);
        antRect.localRotation = Quaternion.identity;
        tool.ant = antRect;
        PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
        var compassPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        string handPath = $"{Root}/Prefabs/Special items.prefab";
        var items = PrefabUtility.LoadPrefabContents(handPath);
        var hand = items.GetComponentInChildren<SpecialItemsHand>(true);
        var existing = items.GetComponentsInChildren<CompassTool>(true).FirstOrDefault();
        if (!existing)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(compassPrefab, items.transform);
            var reference = items.GetComponentsInChildren<CassettePlayerTool>(true).FirstOrDefault();
            if (reference)
            {
                instance.transform.SetParent(reference.transform.parent, false);
                instance.transform.SetSiblingIndex(reference.transform.GetSiblingIndex() + 1);
            }
            existing = instance.GetComponent<CompassTool>();
        }
        if (!hand.tools.Contains(existing)) hand.tools = hand.tools.Concat(new SpecialItemTool[] { existing }).ToArray();
        PrefabUtility.SaveAsPrefabAsset(items, handPath);
        PrefabUtility.UnloadPrefabContents(items);
        AssetDatabase.SaveAssets();
        log.AppendLine($"item {item.id} {item.price} {item.unlock}; catalog {catalog.items.Length}; prefab {prefabPath}; tools {hand.tools.Length}");
        log.AppendLine($"base {baseSprite.rect.size} ant {antSprite.rect.size} icon {icon.rect.size}");
        return log.ToString();
    }

    static Sprite Import(string path, string like, int maxSize = 0)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var reference = (TextureImporter)AssetImporter.GetAtPath(like);
        if (!importer || !reference) return null;
        var settings = new TextureImporterSettings();
        reference.ReadTextureSettings(settings);
        importer.SetTextureSettings(settings);
        var platform = reference.GetDefaultPlatformTextureSettings();
        if (maxSize > 0) platform.maxTextureSize = maxSize;
        importer.SetPlatformTextureSettings(platform);
        if (maxSize > 0) foreach (var name in new[] { "Standalone", "iPhone", "Android", "WebGL" })
            {
                var other = importer.GetPlatformTextureSettings(name);
                if (other.overridden) { other.maxTextureSize = maxSize; importer.SetPlatformTextureSettings(other); }
            }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
