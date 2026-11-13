using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Hakaton.Dev {
public static class CardSchoolArtVerifyTemp {
[Serializable] public class Row { public string asset, definition, guid, assignedArtwork, textureType, spriteMode, compression, filter, wrap, format; public int width,height,maxSize; public float ppu; public bool mipmaps, alphaIsTransparency, spriteLoaded, referenceMatches; }
[Serializable] public class Report { public string project; public bool playing; public List<Row> cards = new List<Row>(); }
public static string Run() {
var paths = new string[] {"Assets/Decks/Art/gilding.png","Assets/Decks/Art/discord.png","Assets/Game/Art/strike.png","Assets/Decks/Art/merchant_nose.png","Assets/Decks/Art/marauder.png","Assets/Decks/Art/tar_puddle.png","Assets/Decks/Art/lure.png","Assets/Decks/Art/scarecrow.png","Assets/Decks/Art/tree_awakening.png","Assets/Game/Art/heavy-strike.png","Assets/Decks/Art/fire-realtime.png","Assets/Decks/Art/water-realtime.png","Assets/Decks/Art/08-ice.png","Assets/Decks/Art/07-wind.png"};
var defs = new string[] {"Assets/Game/Data/gilding.asset","Assets/Game/Data/discord.asset","Assets/Game/Data/strike.asset","Assets/Game/Data/merchant_nose.asset","Assets/Game/Data/marauder.asset","Assets/Game/Data/tar_puddle.asset","Assets/Game/Data/lure.asset","Assets/Game/Data/scarecrow.asset","Assets/Game/Data/tree_awakening.asset","Assets/Game/Data/heavy_strike.asset","Assets/Game/Data/fire.asset","Assets/Game/Data/heal.asset","Assets/Game/Data/ice.asset","Assets/Game/Data/impulse.asset"};
var report = new Report { project = Directory.GetCurrentDirectory(), playing = EditorApplication.isPlaying };
for(int i=0;i<paths.Length;i++) {
var path=paths[i]; AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
var importer=(TextureImporter)AssetImporter.GetAtPath(path); var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path); var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
var definition=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(defs[i]); var so=new SerializedObject(definition); var assigned=so.FindProperty("artwork").objectReferenceValue;
report.cards.Add(new Row {asset=path,definition=defs[i],guid=AssetDatabase.AssetPathToGUID(path),assignedArtwork=AssetDatabase.GetAssetPath(assigned),textureType=importer.textureType.ToString(),spriteMode=importer.spriteImportMode.ToString(),compression=importer.textureCompression.ToString(),filter=importer.filterMode.ToString(),wrap=importer.wrapMode.ToString(),width=texture.width,height=texture.height,maxSize=importer.maxTextureSize,ppu=importer.spritePixelsPerUnit,mipmaps=importer.mipmapEnabled,alphaIsTransparency=importer.alphaIsTransparency,format=texture.format.ToString(),spriteLoaded=sprite!=null,referenceMatches=assigned==sprite });
}
File.WriteAllText(Path.GetFullPath("../output/card-designs/schools-2026-11-14/deployment/unity-verification.json"),JsonUtility.ToJson(report,true));
return "verified "+report.cards.Count+" card imports";
}
}
}
