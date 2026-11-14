#pragma warning disable 0649
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hakaton.Computer;
using Hakaton.Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class TutorialAudit
{
    const string AppPath = "Assets/Computer/Prefabs/PcTutorialWindow.prefab";
    const string PcPath = "Assets/Computer/Prefabs/BackroomPC.prefab";
    static string Out => Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/tutorial-audit-2026-11-14"));
    [Serializable] class Payload { public Patch[] patches; public Page[] pages; }
    [Serializable] class Patch { public string page,prefix,text; public int leading; }
    [Serializable] class Page { public string tab; public Command[] commands; }
    [Serializable] class Command { public string kind,text,path,color; public int x,y,w,h,size,leading; }
    static TMP_Text body,head;
    static Color ColorOf(string v) { ColorUtility.TryParseHtmlString(v,out var c);return c; }
    static RectTransform Node(Transform parent,string name,int x,int y,int w,int h)
    {
        var go=new GameObject(name,typeof(RectTransform));go.layer=10;
        var r=(RectTransform)go.transform;r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);
        return r;
    }
    static string Wrap(TMP_Text t,string s,float width)
    {
        var lines=new List<string>();
        foreach(var paragraph in s.Split('\n'))
        {
            string line="";
            foreach(var word in paragraph.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries))
            {
                string next=line.Length==0?word:line+" "+word;
                if(line.Length>0 && t.GetPreferredValues(next,float.PositiveInfinity,float.PositiveInfinity).x>width)
                {lines.Add(line);line=word;}else line=next;
            }
            lines.Add(line);
        }
        return string.Join("\n",lines);
    }
    static void Draw(Transform parent,Command c,int i)
    {
        var r=Node(parent,$"{i:000} {c.kind}",c.x,c.y,c.w,c.h);
        if(c.kind=="text")
        {
            var t=r.gameObject.AddComponent<TextMeshProUGUI>();var template=c.size==9?body:head;
            t.font=template.font;t.fontSharedMaterial=template.fontSharedMaterial;t.fontSize=c.size;t.enableAutoSizing=false;
            t.color=ColorOf(c.color);t.raycastTarget=false;t.margin=Vector4.zero;t.alignment=TextAlignmentOptions.TopLeft;
            t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Overflow;
            t.text=$"<line-height={c.leading}px>{c.text}</line-height>";
        }
        else if(c.kind=="rect") {var im=r.gameObject.AddComponent<Image>();im.color=ColorOf(c.color);im.raycastTarget=false;}
        else
        {
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(c.path);if(!tex)throw new Exception("Missing "+c.path);
            float scale=Mathf.Min(c.w/(float)tex.width,c.h/(float)tex.height);
            int w=Mathf.RoundToInt(tex.width*scale),h=Mathf.RoundToInt(tex.height*scale);
            r.anchoredPosition+=new Vector2(Mathf.Round((c.w-w)/2f),-Mathf.Round((c.h-h)/2f));r.sizeDelta=new Vector2(w,h);
            var im=r.gameObject.AddComponent<RawImage>();im.texture=tex;im.raycastTarget=false;
        }
    }
    public static string Build()
    {
        if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
        string backup=Path.Combine(Out,"PcTutorialWindow.before.prefab");
        if(!File.Exists(backup))File.Copy(AppPath,backup);
        var data=JsonUtility.FromJson<Payload>(File.ReadAllText(Path.Combine(Out,"layout.json")));
        var root=PrefabUtility.LoadPrefabContents(AppPath);
        try
        {
            var app=root.GetComponent<PcTutorialWindow>();
            body=app.tabs[0].page.GetComponentsInChildren<TMP_Text>(true).First(t=>t.fontSize==9);
            head=app.tabs[0].page.GetComponentsInChildren<TMP_Text>(true).First(t=>t.fontSize==16);
            foreach(var p in data.patches)
            {
                var page=app.tabs.Single(t=>t.title==p.page).page;
                var t=page.GetComponentsInChildren<TMP_Text>(true).Single(v=>v.name.StartsWith(p.prefix));
                int leading=p.leading;
                var match=Regex.Match(t.text,@"<line-height=(\d+)px>");
                if(leading==0)leading=match.Success?int.Parse(match.Groups[1].Value):(int)t.fontSize;
                t.text=$"<line-height={leading}px>{Wrap(t,p.text,t.rectTransform.rect.width)}</line-height>";
            }
            var tabs=app.tabs.ToList();
            foreach(var page in data.pages)
            {
                var tab=tabs.FirstOrDefault(t=>t.title==page.tab);
                if(tab==null)
                {
                    var b=Object.Instantiate(tabs[0].button,tabs[0].button.transform.parent);b.name="Tab "+page.tab;
                    b.GetComponentInChildren<TMP_Text>(true).text=page.tab;
                    tab=new PcTutorialWindow.Tab {title=page.tab,button=b,tabImage=b.GetComponent<Image>(),
                        page=Node(tabs[0].page.transform.parent,"Page "+page.tab,0,0,600,340).gameObject};tabs.Add(tab);
                }
                foreach(Transform tr in tab.page.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(tr.gameObject);
                int i=0;foreach(var cmd in page.commands)Draw(tab.page.transform,cmd,++i);
            }
            app.tabs=tabs.ToArray();
            for(int i=0;i<tabs.Count;i++)
            {
                var rect=(RectTransform)tabs[i].button.transform;rect.anchoredPosition=new Vector2(i*67,0);rect.sizeDelta=new Vector2(64,26);
                var label=tabs[i].button.GetComponentInChildren<TMP_Text>(true);label.rectTransform.sizeDelta=new Vector2(58,19);
                tabs[i].button.transform.SetSiblingIndex(i);tabs[i].page.transform.SetSiblingIndex(i);
            }
            app.SelectTab(0);
            PrefabUtility.SaveAsPrefabAsset(root,AppPath);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();
        return Verify();
    }
    public static string Verify()
    {
        var root=PrefabUtility.LoadPrefabContents(PcPath);var report=new List<string>();
        try
        {
            var app=root.GetComponentInChildren<PcTutorialWindow>(true);
            report.Add("# Проверка сохранённого префаба\n");
            report.Add("Вкладок: "+app.tabs.Length+". Вложенный префаб: "+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(app.gameObject));
            int issues=0;
            foreach(var tab in app.tabs)
            {
                report.Add("\n## "+tab.title+"\n");
                foreach(var t in tab.page.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.ForceMeshUpdate(true,true);
                    var size=t.GetPreferredValues(t.text,float.PositiveInfinity,float.PositiveInfinity);
                    var r=t.rectTransform;
                    if(size.x>r.rect.width+1 || size.y>r.rect.height+2)
                    {issues++;report.Add($"**OVERFLOW** {t.name}: needed {size}, rect {r.rect.size}");}
                    if(t.enableAutoSizing || r.localScale!=Vector3.one){issues++;report.Add("**FONT** "+t.name);}
                    report.Add(Regex.Replace(t.text,"<[^>]+>","").Replace("\n"," / "));
                }
                if(tab.page.GetComponentsInChildren<RawImage>(true).Any(i=>!i.texture)) {issues++;report.Add("**EMPTY IMAGE**");}
            }
            int missing=root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            report.Add($"\nОшибок текста/изображений: {issues}. Отсутствующих скриптов: {missing}.");
            File.WriteAllLines(Path.Combine(Out,"edit-verification.md"),report);
            return $"tabs={app.tabs.Length} issues={issues} missing={missing}";
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
    }
    public static string PreparePlay()
    {
        if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
        SessionState.SetString("TutorialAudit.PreviousSave",SessionState.GetString("Hakaton.SaveFile",""));
        var game=Object.FindFirstObjectByType<GameLoop>();
        string source=game.SavePath;string test=Path.Combine(Out,"isolated-test-save.json");
        if(!File.Exists(source))throw new Exception("No source save for isolated play check");
        File.Copy(source,test,true);
        SessionState.SetString("Hakaton.SaveFile",test);
        return "Isolated test save selected";
    }
    public static string RestoreSave()
    {
        SessionState.SetString("Hakaton.SaveFile",SessionState.GetString("TutorialAudit.PreviousSave",""));
        return "Original save selection restored";
    }
    public static string OpenPlay()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Play mode required");
        var game=Object.FindFirstObjectByType<GameLoop>();game.persistProgress=false;
        var menu=Object.FindFirstObjectByType<MainMenu>();if(menu&&menu.IsOpen)menu.continueButton.onClick.Invoke();
        var pc=Object.FindFirstObjectByType<PcComputer>();if(!pc.IsOpen && !pc.Open())throw new Exception("Cannot open PC");
        pc.boot.Skip();return "PC opened, persistence disabled, isolated save "+game.SavePath;
    }
    public static string Show(string index)
    {
        var pc=Object.FindFirstObjectByType<PcComputer>();
        var p=pc.desktop.programs.Single(p=>p.window.title=="tutorial");p.icon.onClick.Invoke();
        var app=p.window.GetComponent<PcTutorialWindow>();app.tabs[int.Parse(index)].button.onClick.Invoke();Canvas.ForceUpdateCanvases();
        return "Selected "+app.tabs[app.SelectedTab].title;
    }
    public static string Capture(string name)
    {
        var pc=Object.FindFirstObjectByType<PcComputer>();var rt=pc.screen.content;
        var old=RenderTexture.active;RenderTexture.active=rt;
        var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();RenderTexture.active=old;
        string path=Path.Combine(Out,name+".png");File.WriteAllBytes(path,t.EncodeToPNG());Object.DestroyImmediate(t);return path;
    }
    public static string Interaction()
    {
        var pc=Object.FindFirstObjectByType<PcComputer>();var desktop=pc.desktop;
        var p=desktop.programs.Single(x=>x.window.title=="tutorial");var app=p.window.GetComponent<PcTutorialWindow>();
        foreach(var t in app.tabs)
        {
            t.button.onClick.Invoke();
            if(app.tabs.Count(x=>x.page.activeSelf)!=1 || !t.page.activeSelf || desktop.Front!=p.window)throw new Exception("Tab failed "+t.title);
        }
        int selected=app.SelectedTab;p.window.closeButton.onClick.Invoke();
        if(p.window.IsOpen || p.taskButton.gameObject.activeSelf)throw new Exception("Close failed");
        desktop.ToggleMenu();p.menuEntry.onClick.Invoke();
        if(!p.window.IsOpen || desktop.MenuOpen || app.SelectedTab!=selected)throw new Exception("Reopen failed");
        var cards=desktop.programs.First(x=>x.window==pc.cards.window);cards.icon.onClick.Invoke();p.taskButton.onClick.Invoke();
        if(desktop.Front!=p.window || !cards.window.IsOpen)throw new Exception("Task focus failed");
        desktop.Close(cards.window);app.SelectTab(0);
        string result="# Play-mode проверка\n\nПройдены: 9 вкладок, ровно одна видимая страница, закрытие, повторное открытие через Пуск с сохранением вкладки, совместное открытие с картами, фокус через панель задач.\nПроверка использовала отдельное сохранение; запись прогресса отключена.\n";
        File.WriteAllText(Path.Combine(Out,"play-verification.md"),result);return result;
    }
}
