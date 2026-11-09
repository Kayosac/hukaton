using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>GPU regressions for two held drawings, independent finishes and fixed card coverage.</summary>
    public sealed class CardSurfaceTests
    {
        const int Size = 128;
        static readonly string[] Toggles = { "_EmissionEnabled", "_HologramEnabled", "_FoilEnabled", "_ShineEnabled" };
        readonly List<Object> owned = new List<Object>();
        Material material;
        Mesh quad;
        Vector4 originalTime, originalCamera;
        float originalGuiZTest;

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        [SetUp]
        public void Setup()
        {
            originalGuiZTest = Shader.GetGlobalFloat("unity_GUIZTestMode");
            originalTime = Shader.GetGlobalVector("_Time");
            originalCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
            var shader = Shader.Find("Hakaton/Card");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            material = Own(new Material(shader));
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_InkProtection", 0);
            material.SetFloat("_AnimationFPS", 8);
            material.SetFloat("_Boil", 2.5f);
            var texture = Own(new Texture2D(Size, Size, TextureFormat.RGBA32, false, true));
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[Size*Size];
            for (int y=0;y<Size;y++) for (int x=0;x<Size;x++)
            {
                bool inside = x>8 && x<Size-9 && y>8 && y<Size-9 && !(x<28 && y>70 && y<85);
                float line = (x/4 + y/7)%2 == 0 ? .12f : .38f;
                pixels[y*Size+x] = x>=55 && x<73 && y>=55 && y<73
                    ? new Color(1,.28f,.005f,1)
                    : new Color(line, .15f + x/(float)Size*.1f, .12f, inside ? 1 : 0);
            }
            texture.SetPixels(pixels); texture.Apply();
            material.SetTexture("_MainTex", texture);
            material.SetTexture("_BackTex", Texture2D.grayTexture);
            quad = Own(new Mesh());
            quad.vertices = new[]{new Vector3(-1,-1,0),new Vector3(-1,1,0),new Vector3(1,1,0),new Vector3(1,-1,0)};
            quad.uv = new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            quad.colors = Enumerable.Repeat(Color.white,4).ToArray();
            quad.triangles = new[]{0,1,2,0,2,3};
            quad.RecalculateBounds();
        }

        [TearDown]
        public void Teardown()
        {
            Shader.SetGlobalFloat("unity_GUIZTestMode", originalGuiZTest);
            Shader.SetGlobalVector("_Time", originalTime);
            Shader.SetGlobalVector("_WorldSpaceCameraPos", originalCamera);
            foreach (var o in owned) if(o) Object.DestroyImmediate(o);
            owned.Clear();
        }

        Color32[] Render(float time, bool reverse=false, Vector3? camera=null) =>
            RenderColours(time,reverse,camera).Select(c=>(Color32)c).ToArray();

        Color[] RenderColours(float time, bool reverse=false, Vector3? camera=null, bool precise=false)
        {
            var target = RenderTexture.GetTemporary(Size,Size,0,precise ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var old = RenderTexture.active;
            using(var command = new CommandBuffer())
            {
                command.SetRenderTarget(target);
                command.ClearRenderTarget(true,true,Color.clear);
                command.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                command.SetGlobalVector("_Time",new Vector4(time/20,time,time*2,time*3));
                command.SetGlobalVector("_WorldSpaceCameraPos", camera ?? new Vector3(0,0,-10));
                command.DrawMesh(quad, reverse ? Matrix4x4.Scale(new Vector3(-1,1,1)) : Matrix4x4.identity, material,0,0);
                Graphics.ExecuteCommandBuffer(command);
            }
            var read = new Texture2D(Size,Size,precise ? TextureFormat.RGBAFloat : TextureFormat.RGBA32,false,true);
            try
            {
                RenderTexture.active=target;
                read.ReadPixels(new Rect(0,0,Size,Size),0,0); read.Apply();
                return read.GetPixels();
            }
            finally
            {
                RenderTexture.active=old;
                Object.DestroyImmediate(read);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static int Changed(Color32[] a,Color32[] b) => a.Zip(b,(x,y)=>x.Equals(y)?0:1).Sum();
        static void SameAlpha(Color32[] a,Color32[] b) => Assert.That(a.Select(c=>c.a),Is.EqualTo(b.Select(c=>c.a)));

        [Test]
        public void DrawingIsHeldThenAlternatesAndRepeatsWithoutChangingCoverageOrTextRegions()
        {
            var a=Render(.01f); var held=Render(.10f); var b=Render(.14f); var repeated=Render(.26f);
            Assert.That(a.Count(c=>c.a>0),Is.GreaterThan(5000),"Actual GPU output required.");
            Assert.That(Changed(a,held),Is.Zero,"No smooth interpolation within a drawing frame.");
            Assert.That(Changed(a,b),Is.GreaterThan(100),"Second drawing must visibly change the illustration.");
            Assert.That(Changed(a,repeated),Is.Zero,"Exactly two repeatable drawings.");
            SameAlpha(a,b);
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
                if (y<Size*.20f || y>Size*.80f)
                    Assert.That(a[y*Size+x],Is.EqualTo(b[y*Size+x]),"Baked header/footer stay stable.");
            material.SetFloat("_MotionEnabled",0);
            Assert.That(Changed(Render(.01f),Render(.14f)),Is.Zero,"Animation toggle fully disables movement.");
        }

        [Test]
        public void EveryFinishWorksAloneAndAllSixteenCombinationsPreserveAlpha()
        {
            material.SetFloat("_MotionEnabled",0);
            var baseline=Render(1.4f);
            foreach(string toggle in Toggles)
            {
                material.SetFloat(toggle,1);
                var effect=Render(1.4f);
                Assert.That(Changed(baseline,effect),Is.GreaterThan(100),toggle+" works independently");
                SameAlpha(baseline,effect);
                material.SetFloat(toggle,0);
                Assert.That(Changed(baseline,Render(1.4f)),Is.Zero,toggle+" disables cleanly");
            }
            for(int bits=0;bits<16;bits++)
            {
                for(int j=0;j<4;j++) material.SetFloat(Toggles[j],(bits>>j)&1);
                SameAlpha(baseline,Render(1.4f));
            }
        }

        [Test]
        public void BackOptInAndGameplayFeedbackAreIndependent()
        {
            material.SetFloat("_MotionEnabled",0);
            var baseline=Render(1.4f); var back=Render(1.4f,true);
            Assert.That(Changed(baseline,back),Is.GreaterThan(1000),"Back texture must actually render.");
            foreach(var toggle in Toggles) material.SetFloat(toggle,1);
            Assert.That(Changed(back,Render(1.4f,true)),Is.Zero,"Coatings are front-only by default.");
            material.SetFloat("_FinishOnBack",1);
            Assert.That(Changed(back,Render(1.4f,true)),Is.GreaterThan(100));
            foreach(var toggle in Toggles) material.SetFloat(toggle,0);
            material.SetFloat("_Glow",.6f);
            Assert.That(Changed(baseline,Render(1.4f)),Is.GreaterThan(100),"Selection glow survives decorative switches.");
            material.SetFloat("_Flash",1);
            var flash=Render(1.4f);
            Assert.That(flash.Where(c=>c.a>0).All(c=>c.r==255 && c.g==255 && c.b==255),Is.True);
        }

        [Test]
        public void HologramAndFoilRespondToCardViewingAngle()
        {
            material.SetFloat("_MotionEnabled",0);
            foreach(string toggle in new[]{"_HologramEnabled","_FoilEnabled"})
            {
                material.SetFloat(toggle,1);
                Assert.That(Changed(Render(1.4f),Render(1.4f,false,new Vector3(7,2,-10))),Is.GreaterThan(100),toggle);
                material.SetFloat(toggle,0);
            }
        }

        [Test]
        public void UiShaderRendersTheSameHeldDrawingsAndFinishesWithOriginalAlpha()
        {
            material.shader=Shader.Find("Hakaton/Card UI");
            Shader.SetGlobalFloat("unity_GUIZTestMode",(float)CompareFunction.Always);
            var a=Render(.01f);var b=Render(.14f);
            Assert.That(Changed(a,b),Is.GreaterThan(100));
            SameAlpha(a,b);
            foreach(string toggle in Toggles)
            {
                material.SetFloat(toggle,1);
                Assert.That(Changed(b,Render(.14f)),Is.GreaterThan(50),toggle);
                material.SetFloat(toggle,0);
            }
        }

        [Test]
        public void FinishesUseSpriteFeaturesWithoutExternalMapsAndPreserveBlackInk()
        {
            material.SetFloat("_MotionEnabled",0);
            material.SetFloat("_InkProtection",1);
            Assert.That(material.HasProperty("_FinishMask"),Is.False,"Source sprite is the only surface map.");
            var texture=Own(new Texture2D(16,16,TextureFormat.RGBA32,false,true));
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.SetPixels(Enumerable.Repeat(Color.black,256).ToArray());texture.Apply();
            material.SetTexture("_MainTex",texture);
            var black=Render(1.4f);
            foreach(string toggle in Toggles)
            {
                material.SetFloat(toggle,1);
                Assert.That(Changed(black,Render(1.4f)),Is.Zero,toggle+" must preserve source black ink.");
                material.SetFloat(toggle,0);
            }
            texture.SetPixels(Enumerable.Repeat(new Color(.32f,.2f,.08f,1),256).ToArray());texture.Apply();
            // Uniform foil has no relief to emboss. Holography intentionally adds spectral bands.
            material.SetFloat("_FoilEnabled",1);
            var flat=Render(1.4f,false,new Vector3(0,0,-100000));
            var sample=flat[64*Size+64];
            for(int y=56;y<72;y++)for(int x=56;x<72;x++)
            {
                var c=flat[y*Size+x];
                Assert.That(Mathf.Abs(c.r-sample.r)+Mathf.Abs(c.g-sample.g)+Mathf.Abs(c.b-sample.b),
                    Is.LessThanOrEqualTo(3),"Flat pigment must not acquire random embossed texture.");
            }
            material.SetFloat("_FoilEnabled",0);
        }

        [Test]
        public void PresetsSubstantiallyChangeRealCardArtAndKeepCoverage()
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Decks/Art/fire-realtime.png");
            material.SetTexture("_MainTex",sprite);
            material.SetFloat("_MotionEnabled",0);
            material.SetFloat("_InkProtection",1);
            var baseline=Render(1.4f);
            foreach(string toggle in new[]{"_FoilEnabled","_ShineEnabled"})
            {
                material.SetFloat(toggle,1);
                var effect=Render(1.4f);
                var differences=baseline.Zip(effect,(a,b)=>a.a==0 ? -1f :
                    (Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/(3f*255f)).Where(v=>v>=0).ToArray();
                float mean=differences.Average();
                float affected=differences.Count(v=>v>.08f)/(float)differences.Length;
                TestContext.WriteLine(toggle+" mean RGB change="+mean+" area changed >8%="+affected);
                Assert.That(mean,Is.GreaterThan(.055f),toggle+" must materially change the actual card.");
                Assert.That(affected,Is.GreaterThan(.2f),toggle+" must affect a substantial part of the card.");
                SameAlpha(baseline,effect);
                material.SetFloat(toggle,0);
            }
        }

        [TestCase("fire-realtime.png")]
        [TestCase("08-ice.png")]
        [TestCase("tree_awakening.png")]
        public void HologramHasSpectralColourAndPreservesBrightAccents(string file)
        {
            material.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Decks/Art/"+file));
            material.SetFloat("_MotionEnabled",0);
            material.SetFloat("_InkProtection",1);
            material.SetFloat("_HologramHueShift",12);
            var baseline=RenderColours(.1f,precise:true);
            material.SetFloat("_HologramEnabled",1);
            foreach(float time in new[]{.1f,1.4f,3.8f})
            {
                var changed=RenderColours(time,false,new Vector3(5,2,-10),true);
                Assert.That(changed.Select(c=>c.a),Is.EqualTo(baseline.Select(c=>c.a)));
                int spectral=0,accents=0;
                for(int i=0;i<baseline.Length;i++)
                {
                    if(baseline[i].a<.98f)continue;
                    Color.RGBToHSV(baseline[i].gamma,out float h0,out float s0,out float v0);
                    if(s0<.25f || v0<.30f)continue;
                    Color.RGBToHSV(changed[i].gamma,out float h1,out float s1,out float v1);
                    float hueChange=Mathf.Abs(Mathf.DeltaAngle(h0*360,h1*360));
                    if(hueChange>35)spectral++;
                    Assert.That(s1,Is.GreaterThanOrEqualTo(s0-.005f),"Spectral colours must not turn muddy.");
                    if(v0>=.85f)
                    {
                        Assert.That(hueChange,Is.LessThanOrEqualTo(12.5f),"Keep bright focal pigment: "+file);
                        accents++;
                    }
                }
                TestContext.WriteLine(file+" at "+time+": spectral pixels="+spectral+", protected bright pixels="+accents);
                Assert.That(spectral,Is.GreaterThan(80),"A hologram needs visible hue changes, not only changing brightness.");
                Assert.That(accents,Is.GreaterThan(100));
            }
        }

        [Test]
        public void GlowIsBrightAtTheRimAndItsWavesTravelInward()
        {
            material.SetFloat("_MotionEnabled",0);
            material.SetFloat("_InkProtection",1);
            material.SetVector("_MotionRect",new Vector4(0,0,1,1));
            material.SetFloat("_EmissionSpeed",1);
            material.SetFloat("_EmissionPulse",1);
            var texture=Own(new Texture2D(Size,Size,TextureFormat.RGBA32,false,true));
            texture.SetPixels(Enumerable.Repeat(new Color(.025f,.08f,.12f,1),Size*Size).ToArray());
            texture.Apply();material.SetTexture("_MainTex",texture);
            var baseline=RenderColours(0,precise:true);
            material.SetFloat("_EmissionEnabled",1);
            float rim=0,centre=0;
            for(int frame=0;frame<8;frame++)
            {
                var pixels=RenderColours(frame/8f,precise:true);
                rim+=pixels[64*Size+4].b-baseline[64*Size+4].b;
                centre+=pixels[64*Size+64].b-baseline[64*Size+64].b;
                Assert.That(pixels.Select(c=>c.a),Is.EqualTo(baseline.Select(c=>c.a)));
            }
            Assert.That(rim/8,Is.GreaterThan(1),"HDR edge light must be clearly stronger than the printed colour.");
            Assert.That(rim,Is.GreaterThan(centre*2),"Light originates at the perimeter.");
            float outerPeak=-1,innerPeak=-1,outerTime=0,innerTime=0,outerMin=float.MaxValue;
            for(int frame=0;frame<=40;frame++)
            {
                float time=.2f+frame*.025f;
                var pixels=RenderColours(time,precise:true);
                float outer=pixels[64*Size+16].b,inner=pixels[64*Size+32].b;
                outerMin=Mathf.Min(outerMin,outer);
                if(outer>outerPeak){outerPeak=outer;outerTime=time;}
                if(inner>innerPeak){innerPeak=inner;innerTime=time;}
            }
            TestContext.WriteLine("Mean rim HDR lift="+rim/8+", centre="+centre/8+", wave peaks at "+outerTime+" -> "+innerTime);
            Assert.That(outerPeak-outerMin,Is.GreaterThan(.3f),"Waves must visibly pulse.");
            Assert.That(innerTime-outerTime,Is.InRange(.2f,.8f),"The same crest must reach the inner point later.");
        }

        [Test]
        public void GlowKeepsCaptionInteriorsAndCanStopItsWaves()
        {
            material.SetFloat("_MotionEnabled",0);
            material.SetFloat("_InkProtection",1);
            var baseline=Render(.2f);
            material.SetFloat("_EmissionEnabled",1);
            var glow=Render(.2f);
            foreach(int y in new[]{24,104})for(int x=32;x<96;x++)
                Assert.That(glow[y*Size+x],Is.EqualTo(baseline[y*Size+x]),"Interior of the caption/title remains readable.");
            SameAlpha(baseline,glow);
            material.SetFloat("_EmissionPulse",0);
            Assert.That(Changed(Render(.1f),Render(1.3f)),Is.Zero,"Zero wave energy leaves a steady edge glow.");
            material.SetFloat("_EmissionStrength",0);
            Assert.That(Changed(baseline,Render(.2f)),Is.Zero);
        }

        [Test]
        public void SavedGameplayAndUiMaterialsReferenceTheCardShaders()
        {
            foreach(var path in new[]{"Assets/Game/Materials/Card World.mat","Assets/Game/Materials/Card Viewmodel.mat"})
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(mat.shader.name,Is.EqualTo("Hakaton/Card"));
                Assert.That(ShaderUtil.ShaderHasError(mat.shader),Is.False);
                Assert.That(mat.GetFloat("_MotionEnabled"),Is.EqualTo(1));
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decks/Prefabs/CardTile.prefab");
            var art=prefab.GetComponent<Hakaton.Decks.Unity.DeckCardView>().art;
            Assert.That(art.material.shader.name,Is.EqualTo("Hakaton/Card UI"));
            Assert.That(ShaderUtil.ShaderHasError(art.material.shader),Is.False);
        }
    }
}
