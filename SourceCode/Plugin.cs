using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.Mono;
using UnityEngine;

[BepInPlugin("elpa.machiavillain.jpdirect","MachiaVillain JP Direct Test","0.8.21")]
public class Plugin : BaseUnityPlugin
{
    // UI.Text uses Unity's dynamic OS font. No TextCore atlas or native FontEngine calls.
    readonly Dictionary<string,string> jp = new Dictionary<string,string> {
        {"MAIN_NEW_GAME", "ニューゲーム"},
        {"MAIN_NEW_GAME_SANDBOX", "カスタムマップ"}
    };
    // English labels verified in the v0.8.18 main-menu screenshot.
    // Exact matching avoids guessing unknown localization enum identifiers.
    readonly Dictionary<string,string> menuText = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"New game", "ニューゲーム"},
        {"Custom map", "カスタムマップ"},
        {"Load", "ロード"},
        {"Settings", "設定"},
        {"Exit to desktop", "ゲーム終了"}
    };
    // Exact-match translations only. Unknown game text is recorded, never guessed.
    readonly Dictionary<string,string> gameText = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"Back","戻る"}, {"Cancel","キャンセル"}, {"Confirm","決定"},
        {"OK","OK"}, {"Yes","はい"}, {"No","いいえ"},
        {"Continue","続ける"}, {"Pause","一時停止"},
        {"Resume","再開"}, {"Save","セーブ"}, {"Save game","セーブ"},
        {"Load game","ロード"}, {"Options","オプション"},
        {"Apply","適用"}, {"Close","閉じる"}, {"Start","開始"},
        {"Next","次へ"}, {"Previous","前へ"},
        {"Quit","終了"}, {"Exit","終了"},
        {"Main menu","メインメニュー"}, {"New game","ニューゲーム"},
        {"Custom map","カスタムマップ"}, {"Load","ロード"},
        {"Settings","設定"}, {"Exit to desktop","ゲーム終了"},
        {"Bedroom","寝室"}, {"Training","訓練"}, {"Victims","犠牲者"},
        {"Laboratory","研究室"}, {"Factory","工場"}, {"Kitchen","キッチン"}, {"Kitcehn","キッチン"},
        {"Angry","怒り"}, {"Idle","待機中"}, {"Jump Zone","ジャンプゾーン"},
        {"Room not closed (need walls and door)","部屋が閉じられていません（壁とドアが必要です）"},
        {"Room is not closed (needs walls or door surrounding a full floor)","部屋が閉じられていません（床全体を囲む壁またはドアが必要です）"},
        {"One of your minions needs to sleep! <color=#FFCD00FF>(click for more)</color>","手下の1人が睡眠を必要としています！ <color=#FFCD00FF>（クリックで詳細）</color>"},
        {"The research has been completed for the  <color=#FFCD00FF>(click for more)</color>","研究が完了しました <color=#FFCD00FF>（クリックで詳細）</color>"}
    };
    readonly HashSet<string> captured = new HashSet<string>(StringComparer.Ordinal);
    string capturePath;
    readonly Dictionary<int, Overlay> overlays = new Dictionary<int, Overlay>();
    Font japaneseFont;
    readonly HashSet<int> failedTargets = new HashSet<int>();
    GUIStyle style;
    string status = "JP Direct v0.8.21: waiting";
    bool shuttingDown;
    Type uiTextType;

    sealed class Overlay {
        public Component original;
        public Behaviour originalBehaviour;
        public bool wasEnabled;
        public Component label;
        public GameObject overlayObject;
        public string initialText;
    }

    void Awake() {
        Logger.LogInfo("MachiaVillain JP Direct Test v0.8.21 loaded (UI.Text overlay; FontEngine disabled)");
        StartCoroutine(Run());
    }
    IEnumerator Run() {
        yield return new WaitForSeconds(2f);
        try {
            japaneseFont = Font.CreateDynamicFontFromOSFont(new string[] {"Yu Gothic", "Meiryo", "MS Gothic"}, 40);
            Logger.LogInfo("[FONT] OS dynamic font="+(japaneseFont ? japaneseFont.name : "null"));
            uiTextType=FindType("UnityEngine.UI.Text");
            capturePath=Path.Combine(Paths.BepInExRootPath,"MachiaVillainJP_Untranslated.txt");
            Logger.LogInfo("[CAPTURE] output="+capturePath);
            Logger.LogInfo("[UI] UnityEngine.UI.Text="+(uiTextType==null?"missing":uiTextType.AssemblyQualifiedName));
        } catch(Exception e) {Logger.LogError("[INIT] "+e);}
        while(!shuttingDown) { Apply(); CaptureAndTranslate(); yield return new WaitForSeconds(1f); }
    }
    Type FindType(string name) {
        foreach(var a in AppDomain.CurrentDomain.GetAssemblies()) {
            var t=a.GetType(name,false); if(t!=null)return t;
        }
        return null;
    }
    object Get(object obj,string name) {
        if(obj==null)return null;
        for(var t=obj.GetType();t!=null;t=t.BaseType) {
            var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(f!=null)return f.GetValue(obj);
            var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(p!=null&&p.CanRead&&p.GetIndexParameters().Length==0)return p.GetValue(obj,null);
        }
        return null;
    }
    void Set(object obj,string name,object value) {
        var p=obj.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(p==null||!p.CanWrite)throw new MissingMemberException(obj.GetType().FullName,name);
        p.SetValue(obj,value,null);
    }
    bool IsTMP(Component c) {
        if(!c)return false;
        for(var t=c.GetType();t!=null;t=t.BaseType)
            if(t.FullName=="TMPro.TextMeshProUGUI"||t.FullName=="TMPro.TextMeshPro")return true;
        return false;
    }
    Component ResolveTMP(Component loc) {
        object direct=Get(loc,"m_oTextMeshPro");
        if(direct is Component && IsTMP((Component)direct))return (Component)direct;
        foreach(var c in loc.GetComponents<Component>())if(IsTMP(c))return c;
        foreach(var c in loc.GetComponentsInChildren<Component>(true))if(IsTMP(c))return c;
        return null;
    }
    void Apply() {
        if(shuttingDown||uiTextType==null||!japaneseFont)return;
        int found=0,active=0,errors=0;
        foreach(var loc in Resources.FindObjectsOfTypeAll<Component>()) {
            if(!loc||loc.GetType().Name!="TextLoc")continue;
            object key=Get(loc,"m_oLocalizeEnum"); string val=null;
            Component tmp=null;
            if(key!=null)jp.TryGetValue(key.ToString(),out val);
            // Only resolve an actual TextLoc TMP component; never scan arbitrary UI objects.
            if(val==null) {
                tmp=ResolveTMP(loc);
                if(!tmp)continue;
                var english=Get(tmp,"text") as string;
                if(english==null||!menuText.TryGetValue(english.Trim(),out val))continue;
            }
            found++;
            if(!tmp)tmp=ResolveTMP(loc);
            if(!tmp)continue;
            int id=tmp.GetInstanceID();
            Overlay overlay;
            if(!overlays.TryGetValue(id,out overlay)) {
                if(failedTargets.Contains(id))continue;
                try {
                    // A separate child object is mandatory: the TMP object already owns a Graphic.
                    // Adding UI.Text to the same GameObject can fail due to Unity component constraints.
                    GameObject child = new GameObject("MachiaVillainJP_TextOverlay", typeof(RectTransform));
                    try {
                        child.transform.SetParent(tmp.transform, false);
                        var rt=child.GetComponent<RectTransform>();
                        rt.anchorMin=Vector2.zero;
                        rt.anchorMax=Vector2.one;
                        rt.offsetMin=Vector2.zero;
                        rt.offsetMax=Vector2.zero;
                        rt.localScale=Vector3.one;
                        var label=child.AddComponent(uiTextType) as Component;
                        if(label==null)throw new Exception("UI.Text child component creation returned null");
                    Set(label,"font",japaneseFont);
                    Set(label,"text",val);
                    Set(label,"fontSize",Math.Max(12,(int)Math.Round(Convert.ToDouble(Get(tmp,"fontSize")??36))));
                    Set(label,"color",Get(tmp,"color")??Color.white);
                    Set(label,"raycastTarget",false);
                    Set(label,"supportRichText",false);
                    var alignmentType=FindType("UnityEngine.TextAnchor");
                    if(alignmentType!=null)Set(label,"alignment",Enum.Parse(alignmentType,"MiddleCenter"));
                    var behaviour=tmp as Behaviour;
                    overlay=new Overlay { original=tmp,originalBehaviour=behaviour,wasEnabled=behaviour!=null&&behaviour.enabled,
                        label=label,overlayObject=child,initialText=Get(tmp,"text") as string };
                    overlays[id]=overlay;
                    if(behaviour!=null)behaviour.enabled=false;
                    Logger.LogInfo("[OVERLAY] child created go="+tmp.gameObject.name+" text="+val+
                        " font="+japaneseFont.name+" size="+Get(label,"fontSize"));
                    } catch { Destroy(child); throw; }
                } catch(Exception ex) {errors++;failedTargets.Add(id);Logger.LogError("[OVERLAY] failed go="+tmp.gameObject.name+" "+ex);continue;}
            } else {
                try {
                    if(overlay.label) {
                        if((Get(overlay.label,"text") as string)!=val)Set(overlay.label,"text",val);
                        if(overlay.originalBehaviour&&overlay.originalBehaviour.enabled)overlay.originalBehaviour.enabled=false;
                    }
                } catch(Exception ex) {errors++;Logger.LogError("[OVERLAY] refresh "+ex);}
            }
            active++;
        }
        status="JP Direct v0.8.21: found="+found+" overlay="+active+" errors="+failedTargets.Count;
    }
    bool TryTranslateGameText(string source, out string translated) {
        if(gameText.TryGetValue(source,out translated))return true;
        // Preserve TMP sprite/color markup while translating changing numeric values.
        const string prestigePrefix="<SPRITE=\"ICON_Prestige\" index=0>Prestige: ";
        if(source.StartsWith(prestigePrefix,StringComparison.OrdinalIgnoreCase)) {
            translated="<SPRITE=\"ICON_Prestige\" index=0>名声: "+source.Substring(prestigePrefix.Length);
            return true;
        }
        if(source.StartsWith("Prestige : ",StringComparison.OrdinalIgnoreCase)) {
            translated="名声 : "+source.Substring(11); return true;
        }
        if(source.StartsWith("Evilness : ",StringComparison.OrdinalIgnoreCase)) {
            translated="邪悪度 : "+source.Substring(11); return true;
        }
        translated=null; return false;
    }
    void CaptureAndTranslate() {
        if(shuttingDown || uiTextType==null || !japaneseFont)return;
        foreach(var c in Resources.FindObjectsOfTypeAll<Component>()) {
            if(!c || !IsTMP(c) || !c.gameObject.scene.IsValid())continue;
            string english=Get(c,"text") as string;
            if(string.IsNullOrWhiteSpace(english))continue;
            string trimmed=english.Trim();
            if(trimmed.Length>160 || trimmed.IndexOf('\n')>=0 || trimmed.IndexOf('\r')>=0)continue;
            // Preserve a bounded list of observed strings, even if not translated yet.
            if(captured.Count<1200 && captured.Add(trimmed)) {
                try { File.AppendAllText(capturePath, trimmed.Replace("\t"," ")+Environment.NewLine, new UTF8Encoding(false)); }
                catch(Exception ex) { if(captured.Count==1)Logger.LogWarning("[CAPTURE] "+ex.Message); }
            }
            string translated;
            if(!TryTranslateGameText(trimmed,out translated))continue;
            int id=c.GetInstanceID();
            if(overlays.ContainsKey(id)||failedTargets.Contains(id))continue;
            GameObject child=null;
            try {
                child=new GameObject("MachiaVillainJP_GameTextOverlay",typeof(RectTransform));
                child.transform.SetParent(c.transform,false);
                var rt=child.GetComponent<RectTransform>();
                rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one;
                rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
                rt.localScale=Vector3.one;
                var label=child.AddComponent(uiTextType) as Component;
                if(!label)throw new Exception("UI.Text component not created");
                Set(label,"font",japaneseFont);
                Set(label,"text",translated);
                Set(label,"fontSize",Math.Max(12,(int)Math.Round(Convert.ToDouble(Get(c,"fontSize")??28))));
                Set(label,"color",Get(c,"color")??Color.white);
                Set(label,"raycastTarget",false);
                Set(label,"supportRichText",false);
                var anchor=FindType("UnityEngine.TextAnchor");
                if(anchor!=null)Set(label,"alignment",Enum.Parse(anchor,"MiddleCenter"));
                var behaviour=c as Behaviour;
                overlays[id]=new Overlay { original=c, originalBehaviour=behaviour,
                    wasEnabled=behaviour!=null&&behaviour.enabled,
                    label=label,overlayObject=child,initialText=english };
                if(behaviour!=null)behaviour.enabled=false;
                Logger.LogInfo("[GAME JP] "+trimmed+" -> "+translated+" ("+c.gameObject.name+")");
            } catch(Exception ex) {
                if(child)Destroy(child);
                failedTargets.Add(id);
                Logger.LogWarning("[GAME JP] failed "+trimmed+": "+ex.Message);
            }
        }
    }
    void Restore() {
        if(shuttingDown)return;
        shuttingDown=true;
        foreach(var kv in overlays) {
            try {
                var x=kv.Value;
                if(x.originalBehaviour)x.originalBehaviour.enabled=x.wasEnabled;
                // The overlay component was created by us on the existing GameObject.
                if(x.overlayObject)Destroy(x.overlayObject);
            } catch(Exception e) {Logger.LogWarning("[RESTORE] "+e.Message);}
        }
        overlays.Clear();
        failedTargets.Clear();
    }
    void OnDisable() { Restore(); }
    void OnDestroy() { Restore(); }
    void OnGUI() {
        if(style==null){style=new GUIStyle(GUI.skin.label);style.fontSize=17;style.normal.textColor=Color.white;}
        GUI.Box(new Rect(10,10,700,38),"");
        GUI.Label(new Rect(20,18,680,25),status,style);
    }
}
