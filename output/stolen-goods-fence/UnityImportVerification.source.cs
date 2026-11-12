using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.Dev
{
    public static class FenceImportVerification
    {
        [Serializable] public class Frame { public string name; public Rect rect; public Vector2 pivot; public float ppu; public long localId; }
        [Serializable] public class Report
        {
            public string editorVersion, asset, textureType, spriteMode, compression, filter, npot;
            public int width, height, maxTextureSize;
            public bool verified, mipmaps, equalCells, commonPivot, switchTransformStable, playModeValidated;
            public Frame[] sprites;
            public string preview, scope;
        }
        public static void Run()
        {
            const string path="Assets/Content/NPCs/StolenGoodsFence/StolenGoodsFenceAtlas.png";
            string dest=Path.GetFullPath("../output/stolen-goods-fence");
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            string[] suffixes={"DefaultFront","Back","DispleasedFront","HappyFront","EnragedFront"};
            var assets=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            var sprites=suffixes.Select(n=>assets.Single(s=>s.name=="StolenGoodsFence_"+n)).ToArray();
            bool equal=sprites.All(s=>s.rect.size==sprites[0].rect.size);
            bool pivots=sprites.All(s=>Vector2.Distance(s.pivot,sprites[0].pivot)<.001f);
            if(assets.Length!=5 || !equal || !pivots || importer.spriteImportMode!=SpriteImportMode.Multiple || importer.npotScale!=TextureImporterNPOTScale.None)
                throw new Exception("Unexpected sprite import configuration");
            var scene=EditorSceneManager.NewPreviewScene();
            var oldRT=RenderTexture.active;
            RenderTexture rt=null;Texture2D capture=null;Material mat=null;
            bool stable=true;
            try
            {
                var rig=new GameObject("FenceImportProbe");SceneManager.MoveGameObjectToScene(rig,scene);
                var renderer=rig.AddComponent<SpriteRenderer>();
                var before=rig.transform.localToWorldMatrix;
                foreach(var sprite in sprites){renderer.sprite=sprite;stable &= before==rig.transform.localToWorldMatrix;}
                UnityEngine.Object.DestroyImmediate(rig);
                mat=new Material(Shader.Find("Sprites/Default"));
                for(int i=0;i<sprites.Length;i++)
                {
                    var go=new GameObject(sprites[i].name);SceneManager.MoveGameObjectToScene(go,scene);
                    go.transform.position=new Vector3(i*11.5f,0,0);
                    var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprites[i];sr.sharedMaterial=mat;
                }
                var camGO=new GameObject("FencePreviewCamera");SceneManager.MoveGameObjectToScene(camGO,scene);
                var cam=camGO.AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=7.5f;
                cam.transform.position=new Vector3(23,6.5f,-20);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.88f,.85f,.8f,1);
                rt=new RenderTexture(1600,400,24,RenderTextureFormat.ARGB32);cam.targetTexture=rt;
                cam.scene=scene;cam.Render();RenderTexture.active=rt;
                capture=new Texture2D(1600,400,TextureFormat.RGBA32,false);capture.ReadPixels(new Rect(0,0,1600,400),0,0);capture.Apply();
                File.WriteAllBytes(Path.Combine(dest,"unity-import-preview.png"),capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=oldRT;
                EditorSceneManager.ClosePreviewScene(scene);
                if(rt)UnityEngine.Object.DestroyImmediate(rt);if(capture)UnityEngine.Object.DestroyImmediate(capture);if(mat)UnityEngine.Object.DestroyImmediate(mat);
            }
            var report=new Report{editorVersion=Application.unityVersion,asset=path,width=texture.width,height=texture.height,
                textureType=importer.textureType.ToString(),spriteMode=importer.spriteImportMode.ToString(),maxTextureSize=importer.maxTextureSize,
                compression=importer.textureCompression.ToString(),filter=importer.filterMode.ToString(),npot=importer.npotScale.ToString(),mipmaps=importer.mipmapEnabled,
                equalCells=equal,commonPivot=pivots,switchTransformStable=stable,verified=true,playModeValidated=false,
                preview="unity-import-preview.png",scope="Actual Editor asset import, five sprite rects and pivots, sequential transform stability and offscreen rendering. No gameplay or roster integration.",
                sprites=sprites.Select(s=>{AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s,out string guid,out long id);return new Frame{name=s.name,rect=s.rect,pivot=s.pivot,ppu=s.pixelsPerUnit,localId=id};}).ToArray()};
            File.WriteAllText(Path.Combine(dest,"unity-verification.json"),JsonUtility.ToJson(report,true));
            Debug.Log("Fence import verified: "+texture.width+"x"+texture.height+", 5 equal sprites, PPU "+sprites[0].pixelsPerUnit);
        }
    }
}
