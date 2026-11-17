using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Hakaton.Dev {
public static class AnthillArtVerification {
    static readonly string[] Names={"AntWorker","AntPlug","WingedAnt","BubbleLarva","AntQueen","AntMachineGunCrew"};
    [Serializable] class Cell { public string name; public int[] rect; public float[] pivot; }
    [Serializable] class Manifest { public int cell_width,cell_height,width,height; public Cell[] cells; }
    [Serializable] class Frame { public string name,guid; public Rect rect; public Vector2 pivot; public float ppu; public long fileID; }
    [Serializable] class Report {
        public string status,editorVersion,activeScene,textureType,spriteMode,filter,compression,npot,preview,scope;
        public int width,height,spriteCount,maxTextureSize;
        public bool verifiedInEditor,editorWasPlaying,mipmaps,sourceHasAlpha,rectsEqual,pivotsEqual,ppuEqual,boundsEqual,switchTransformStable,gameplayVerified,temporaryAuthoringScriptRemoved;
        public Frame[] sprites;
    }
    static string Pack=>Path.GetFullPath("../output/monsters/anthill-pack");
    public static void Run() {
        if(EditorApplication.isPlaying || EditorApplication.isCompiling) throw new Exception("Wait for stable Edit mode");
        foreach(string name in Names) {
            string folder="Assets/Content/Enemies/"+name;
            string path=folder+"/"+name+"Atlas.png";
            var m=JsonUtility.FromJson<Manifest>(File.ReadAllText(folder+"/manifest.json"));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);
            if(name!="AntMachineGunCrew") {
                imp.textureType=TextureImporterType.Sprite;imp.spriteImportMode=SpriteImportMode.Multiple;
                imp.spritePixelsPerUnit=100;imp.mipmapEnabled=false;imp.alphaIsTransparency=true;
                imp.npotScale=TextureImporterNPOTScale.None;imp.textureCompression=TextureImporterCompression.Uncompressed;
                imp.maxTextureSize=2048;imp.filterMode=FilterMode.Bilinear;imp.wrapMode=TextureWrapMode.Clamp;
                var settings=new TextureImporterSettings();imp.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;imp.SetTextureSettings(settings);
                // Explicit fixed-cell grid; no Automatic slicing, trimming or outline generation.
                #pragma warning disable 618
                imp.spritesheet=m.cells.Select(c=>new SpriteMetaData{name=c.name,rect=new Rect(c.rect[0],c.rect[1],c.rect[2],c.rect[3]),alignment=9,pivot=new Vector2(c.pivot[0],c.pivot[1])}).ToArray();
                #pragma warning restore 618
                imp.SaveAndReimport();
            }
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var all=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            var sprites=m.cells.Select(c=>all.Single(s=>s.name==c.name)).ToArray();
            bool rects=sprites.All(s=>s.rect.width==m.cell_width && s.rect.height==m.cell_height);
            bool pivots=sprites.All(s=>Vector2.Distance(s.pivot,sprites[0].pivot)<.001f);
            bool ppu=sprites.All(s=>Mathf.Approximately(s.pixelsPerUnit,100));
            bool bounds=sprites.All(s=>Vector3.Distance(s.bounds.size,sprites[0].bounds.size)<.001f);
            if(all.Length!=7 || !rects || !pivots || !ppu || !bounds || tex.width!=m.width || tex.height!=m.height)
                throw new Exception("Invalid imported atlas: "+name);
            bool stable=RenderStates(name,sprites);
            var report=new Report{status="verified_actual_editor_import",editorVersion=Application.unityVersion,activeScene=SceneManager.GetActiveScene().path,
                textureType=imp.textureType.ToString(),spriteMode=imp.spriteImportMode.ToString(),filter=imp.filterMode.ToString(),compression=imp.textureCompression.ToString(),npot=imp.npotScale.ToString(),
                width=tex.width,height=tex.height,spriteCount=all.Length,maxTextureSize=imp.maxTextureSize,verifiedInEditor=true,editorWasPlaying=EditorApplication.isPlaying,mipmaps=imp.mipmapEnabled,
                sourceHasAlpha=imp.DoesSourceTextureHaveAlpha(),rectsEqual=rects,pivotsEqual=pivots,ppuEqual=ppu,boundsEqual=bounds,switchTransformStable=stable,
                gameplayVerified=false,temporaryAuthoringScriptRemoved=false,preview=name+"/unity-states.png",scope="Actual Editor import and GPU rendering in disposable preview of saved EnemyDebug; main scene untouched; no gameplay verification.",
                sprites=sprites.Select(s=>{AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s,out string guid,out long id);return new Frame{name=s.name,rect=s.rect,pivot=s.pivot,ppu=s.pixelsPerUnit,guid=guid,fileID=id};}).ToArray()};
            string outdir=Path.Combine(Pack,name);Directory.CreateDirectory(outdir);
            File.WriteAllText(Path.Combine(outdir,"unity-verification.json"),JsonUtility.ToJson(report,true));
            if(name!="AntMachineGunCrew")File.WriteAllText(folder+"/unity-verification.json",JsonUtility.ToJson(report,true));
        }
        RenderComparison();AssetDatabase.Refresh();
        File.WriteAllText(Path.Combine(Pack,"editor-complete.json"),"{\"verified\":true,\"enemyCount\":6,\"sprites\":42}");
        Debug.Log("Anthill: six atlases, 42 imported sprites verified; original crew unchanged.");
    }
    static Scene OpenContext() {
        var scene=EditorSceneManager.OpenPreviewScene("Assets/EnemyDebug/Scenes/EnemyDebug.unity");
        foreach(var go in scene.GetRootGameObjects())go.SetActive(false);
        return scene;
    }
    static SpriteRenderer Add(Scene scene,string name,Sprite sprite,Vector3 position,Material mat) {
        var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=position;
        var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sharedMaterial=mat;
        var block=new MaterialPropertyBlock();sr.GetPropertyBlock(block);block.SetTexture("_MainTex",sprite.texture);sr.SetPropertyBlock(block);return sr;
    }
    static bool RenderStates(string name,Sprite[] sprites) {
        var scene=OpenContext();var mat=new Material(Shader.Find("Sprites/Default"));
        try {
            var probe=Add(scene,"SwitchProbe",sprites[0],Vector3.zero,mat);var transform=probe.transform.localToWorldMatrix;
            bool stable=true;foreach(var s in sprites){probe.sprite=s;stable &= probe.transform.localToWorldMatrix==transform;}
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            float cw=sprites[0].rect.width/100f,ch=sprites[0].rect.height/100f;
            for(int i=0;i<sprites.Length;i++)Add(scene,sprites[i].name,sprites[i],new Vector3((i%3)*(cw+.3f),-(i/3)*(ch+.3f),0),mat);
            float viewH=3*(ch+.3f),viewW=3*(cw+.3f);
            Capture(scene,new Vector3(cw+.3f,-ch-.3f+ch*.5f, -20),Mathf.Max(viewH/2,viewW/2/1.5f)*1.08f,1500,1000,Path.Combine(Pack,name,"unity-states.png"));return stable;
        } finally {EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(mat);}
    }
    static void RenderComparison() {
        var scene=OpenContext();var mat=new Material(Shader.Find("Sprites/Default"));
        try {
            string[] names={"PurpleMushroom","AntWorker","AntPlug","AntMachineGunCrew","WingedAnt","BubbleLarva","AntQueen"};float x=0;
            foreach(var name in names){string path="Assets/Content/Enemies/"+name+"/"+name+"Atlas.png";
                var s=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First(v=>v.name.EndsWith("DefaultFront") || v.name.EndsWith("FrontDefault"));
                Add(scene,name,s,new Vector3(x+s.bounds.extents.x,0,0),mat);x+=s.bounds.size.x+.35f;}
            Capture(scene,new Vector3(x/2,1.7f,-20),Mathf.Max(2.3f,x/2/4f),2000,500,Path.Combine(Pack,"unity-scale-comparison.png"));
        }finally{EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(mat);}
    }
    static void Capture(Scene scene,Vector3 pos,float size,int w,int h,string file) {
        var go=new GameObject("AnthillPreviewCamera");SceneManager.MoveGameObjectToScene(go,scene);
        var cam=go.AddComponent<Camera>();cam.scene=scene;cam.orthographic=true;cam.orthographicSize=size;cam.transform.position=pos;
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.88f,.85f,.8f,1);cam.aspect=(float)w/h;
        var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;Texture2D tex=null;
        try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex=new Texture2D(w,h,TextureFormat.RGBA32,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,tex.EncodeToPNG());}
        finally{RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);if(tex)UnityEngine.Object.DestroyImmediate(tex);}
    }
}}
