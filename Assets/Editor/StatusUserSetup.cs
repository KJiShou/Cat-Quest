using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class StatusUserSetup
{
    const string CanvasPath = "Assets/Prefabs/Canvas.prefab";
    static StatusUserSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/setup-status-user.request")) return;
        File.Delete("Temp/setup-status-user.request");
        try { Build(); File.WriteAllText("Temp/setup-status-user.result", "PASS: Lobby user profile saved as first child of Status_Group; player data bindings verified."); }
        catch (Exception ex) { File.WriteAllText("Temp/setup-status-user.result", ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("Tools/Cat Quest/Setup Status User Profile")]
    public static void Build()
    {
        var root = PrefabUtility.LoadPrefabContents(CanvasPath);
        try
        {
            var group = root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Status_Group");
            var row = group.GetComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            var gap = group.Find("UserProfileGap");
            if (!gap)
            {
                var spacer = new GameObject("UserProfileGap", typeof(RectTransform), typeof(LayoutElement));
                spacer.layer = group.gameObject.layer;
                spacer.transform.SetParent(group, false);
                gap = spacer.transform;
            }
            var gapLayout = gap.GetComponent<LayoutElement>();
            gapLayout.minWidth = gapLayout.preferredWidth = 180;
            gapLayout.flexibleWidth = 1;
            var existing = group.Find("UserProfile");
            if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var wrapper = new GameObject("UserProfile", typeof(RectTransform), typeof(LayoutElement));
            wrapper.layer = group.gameObject.layer;
            wrapper.transform.SetParent(group, false);
            wrapper.transform.SetAsFirstSibling();
            gap.SetSiblingIndex(1);
            var rect = (RectTransform)wrapper.transform;
            rect.sizeDelta = new Vector2(355, 110);
            var layout = wrapper.GetComponent<LayoutElement>();
            // Reserve trailing space because Status_Group uses negative spacing for its currency icons.
            layout.minWidth = layout.preferredWidth = 355;
            layout.flexibleWidth = 0;
            var lobby = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Lobby.prefab");
            var source = lobby.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "UserInfo");
            var profile = UnityEngine.Object.Instantiate(source.gameObject, wrapper.transform).GetComponent<RectTransform>();
            profile.name = "UserInfo";
            profile.anchorMin = profile.anchorMax = new Vector2(0, .5f);
            profile.pivot = new Vector2(0, .5f);
            profile.anchoredPosition = Vector2.zero;
            profile.localScale = Vector3.one * .60f;
            foreach (var button in profile.GetComponentsInChildren<Button>(true)) UnityEngine.Object.DestroyImmediate(button);
            foreach (var graphic in profile.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            var portrait = profile.Find("PictureFrame/PictureFrame_(Mask)/Image").GetComponent<Image>();
            portrait.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Packages/Character/Spine 2D Ninja Cats Character Set/Characters/PNG Image Sequence/Ninja/idle/ninja-idle_00.png");
            if (!portrait.sprite) throw new InvalidOperationException("Missing cat portrait sprite.");
            portrait.color = Color.white;
            portrait.type = Image.Type.Simple;
            portrait.preserveAspect = true;
            // Enlarge the existing idle sprite inside the circular mask to frame the cat's face.
            portrait.rectTransform.sizeDelta = new Vector2(500, 375);
            portrait.rectTransform.anchoredPosition = new Vector2(50, -12);
            // The demo's orange bar has no corresponding player stat.
            profile.Find("Slider_Orange").gameObject.SetActive(false);
            var status = wrapper.AddComponent<CatStatusUI>();
            status.playerName = profile.Find("Text").GetComponent<TMP_Text>();
            status.playerName.text = "Player";
            status.playerName.richText = false;
            status.playerName.enableAutoSizing = true;
            status.playerName.fontSizeMin = 22;
            status.playerName.fontSizeMax = 30;
            status.playerName.textWrappingMode = TextWrappingModes.NoWrap;
            status.playerName.overflowMode = TextOverflowModes.Ellipsis;
            status.playerName.rectTransform.sizeDelta = new Vector2(290, 45);
            status.playerName.rectTransform.anchoredPosition = new Vector2(345, -35);
            status.level = profile.Find("PictureFrame/IconFrame02/Text (TMP)").GetComponent<TMP_Text>();
            status.level.text = "LV 1";
            status.level.enableAutoSizing = true;
            status.level.fontSizeMin = 14;
            status.level.fontSizeMax = 22;
            status.level.textWrappingMode = TextWrappingModes.NoWrap;
            status.level.rectTransform.sizeDelta = new Vector2(51, 35);
            status.experienceBar = profile.Find("Slider_Green").GetComponent<Slider>();
            status.experienceBar.interactable = false;
            status.experienceBar.transition = Selectable.Transition.None;
            status.experienceBar.onValueChanged = new Slider.SliderEvent();
            status.experienceBar.maxValue = 100;
            status.experienceBar.SetValueWithoutNotify(0);
            status.experience = profile.Find("Slider_Green/FillArea01/Text_Value").GetComponent<TMP_Text>();
            status.experience.text = "0 / 100";
            var card = root.GetComponentInChildren<MainMenuNavigation>(true).pages[0].transform.Find("Top/Character");
            var old = card.GetComponent<CatStatusUI>();
            if (old)
            {
                if (old.playerName) UnityEngine.Object.DestroyImmediate(old.playerName.gameObject);
                if (card.Find("LevelInfo")) UnityEngine.Object.DestroyImmediate(card.Find("LevelInfo").gameObject);
                UnityEngine.Object.DestroyImmediate(old);
            }
            if (!status.playerName || !status.level || !status.experience || !status.experienceBar || wrapper.transform.GetSiblingIndex() != 0)
                throw new InvalidOperationException("Incomplete profile bindings or incorrect placement.");
            PrefabUtility.SaveAsPrefabAsset(root, CanvasPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
