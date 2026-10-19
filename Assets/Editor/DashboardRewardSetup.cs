using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds editable, serialized UI using this project's existing font, sprites and button prefab.
[InitializeOnLoad]
public static class DashboardRewardSetup
{
    private const string CanvasPath = "Assets/Prefabs/Canvas.prefab";
    private const string IconPath = "Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/512/";
    private static TaskUIBuilder ui;
    private static GameObject buttonPrefab;
    private static Transform mission, userInfo, equipment;
    static DashboardRewardSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists("Temp/setup-dashboard-rewards.request")) return;
        File.Delete("Temp/setup-dashboard-rewards.request");
        try { Build(); File.WriteAllText("Temp/setup-dashboard-rewards.result", "PASS: dashboard, independent goals and Focus UI saved to shared Canvas prefab."); }
        catch (Exception ex) { File.WriteAllText("Temp/setup-dashboard-rewards.result", "FAIL: " + ex); Debug.LogException(ex); }
    }
    [MenuItem("Tools/Cat Quest/Rebuild Dashboard Reward UI")]
    public static void Build()
    {
        var root = PrefabUtility.LoadPrefabContents(CanvasPath);
        try
        {
            var navigation = root.GetComponentInChildren<MainMenuNavigation>(true);
            var taskSystem = navigation.taskSystem;
            ui = new TaskUIBuilder(taskSystem.font, taskSystem.roundedSprite);
            buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Button_Normal.prefab");
            const string panels = "Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/";
            mission = AssetDatabase.LoadAssetAtPath<GameObject>(panels + "Mission.prefab").transform;
            userInfo = AssetDatabase.LoadAssetAtPath<GameObject>(panels + "Lobby_Popup_User_Info.prefab").transform;
            equipment = AssetDatabase.LoadAssetAtPath<GameObject>(panels + "Equipment.prefab").transform;
            navigation.initialPage = MainMenuPage.Dashboard;
            foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
                foreach (var option in dropdown.options) if (option.text == "Normal") option.text = "Medium";
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                if (label.text == "Normal") label.text = "Medium";
            var dashboard = navigation.pages[(int)MainMenuPage.Dashboard].transform;
            var top = dashboard.Find("Top"); var bottom = dashboard.Find("Bottom");
            var topSize = top.GetComponent<LayoutElement>();
            topSize.minHeight = 450; topSize.preferredHeight = 580; topSize.flexibleHeight = 3;
            var bottomSize = bottom.GetComponent<LayoutElement>() ?? bottom.gameObject.AddComponent<LayoutElement>();
            bottomSize.minHeight = 150; bottomSize.preferredHeight = 180; bottomSize.flexibleHeight = 1;
            foreach (Transform card in top)
            {
                var le = card.GetComponent<LayoutElement>(); le.minWidth = 0; le.preferredWidth = 0;
                le.flexibleWidth = card.name == "TodayQuest" ? 1.2f : 1;
                le.minHeight = 0; le.preferredHeight = 0; le.flexibleHeight = 1;
            }
            BuildCharacter(top.Find("Character"));
            BuildGoals(top.Find("DailyReward"));
            var quickCard = bottom.Find("Overview");
            KeepBackground(quickCard);
            quickCard.GetComponent<LayoutElement>().preferredWidth = 0;
            quickCard.GetComponent<LayoutElement>().flexibleWidth = 1.5f;
            BuildQuickFocus(quickCard, navigation);
            var allowanceCard = bottom.Find("Today Quest");
            KeepBackground(allowanceCard);
            allowanceCard.GetComponent<LayoutElement>().preferredWidth = 0;
            allowanceCard.GetComponent<LayoutElement>().flexibleWidth = 1;
            Label(allowanceCard, "Today's Progress", 23, .05f, .70f, .95f, .94f, true);
            var allowance = allowanceCard.gameObject.GetComponent<RewardAllowanceUI>() ?? allowanceCard.gameObject.AddComponent<RewardAllowanceUI>();
            allowance.summary = Label(allowanceCard, "Task rewards: 0/120 EXP | 0/60 Fish Coins\nFocus rewards: 0/120 minutes | 0/4 Tickets\nDaily goal rewards are extra. Goals reset at midnight.", 18, .05f, .08f, .95f, .68f);
            BuildFocus(navigation.pages[(int)MainMenuPage.Focus].transform, navigation);
            PrefabUtility.SaveAsPrefabAsset(root, CanvasPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); ui = null; }
        AssetDatabase.SaveAssets();
    }
    private static void KeepBackground(Transform panel)
    {
        foreach (Transform child in panel.Cast<Transform>().ToArray())
            if (child.name != "background") UnityEngine.Object.DestroyImmediate(child.gameObject);
        var layout = panel.GetComponent<LayoutGroup>(); if (layout) UnityEngine.Object.DestroyImmediate(layout);
    }
    private static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        var rect = TaskUIBuilder.Rect(parent, name); Place(rect, x0, y0, x1, y1); return rect;
    }
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
    }
    private static TMP_Text Label(Transform parent, string text, float size, float x0, float y0, float x1, float y1, bool bold = false)
    {
        var label = ui.Text(parent, text, size);
        Place(label.rectTransform, x0, y0, x1, y1);
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.enableAutoSizing = true; label.fontSizeMax = size; label.fontSizeMin = Math.Max(14, size - 4);
        return label;
    }
    private static Image Icon(Transform parent, string filename, float x0, float y0, float x1, float y1)
    {
        var rect = Rect(parent, filename, x0, y0, x1, y1);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath + filename + ".Png");
        if (!sprite) throw new InvalidOperationException("Missing existing icon: " + filename);
        var icon = ui.Image(rect, Color.white, sprite); icon.type = Image.Type.Simple;
        icon.preserveAspect = true; icon.raycastTarget = false; return icon;
    }
    private static Button Button(Transform parent, string name, string caption, float x0, float y0, float x1, float y1)
    {
        var go = UnityEngine.Object.Instantiate(buttonPrefab, parent); go.name = name;
        var button = go.GetComponentInChildren<Button>(true);
        button.onClick = new Button.ButtonClickedEvent();
        // The navigation prefab's fixed child layout would collapse a text-only action label.
        foreach (var layout in go.GetComponentsInChildren<LayoutGroup>(true)) UnityEngine.Object.DestroyImmediate(layout);
        TaskUIBuilder.Stretch((RectTransform)button.transform, 2, 2, 2, 2);
        var root = (RectTransform)go.transform; Place(root, x0, y0, x1, y1);
        var le = root.GetComponent<LayoutElement>(); if (le) le.ignoreLayout = true;
        foreach (var image in go.GetComponentsInChildren<Image>(true))
            if (image.name == "Icon") image.gameObject.SetActive(false);
        var text = go.GetComponentInChildren<TMP_Text>(true);
        text.text = caption; text.fontSize = 20; text.enableAutoSizing = true; text.fontSizeMin = 15; text.fontSizeMax = 20;
        text.alignment = TextAlignmentOptions.Center;
        TaskUIBuilder.Stretch(text.rectTransform, 8, 8, 2, 2);
        return button;
    }
    private static Slider Bar(Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        var rect = Rect(parent, name, x0, y0, x1, y1);
        ui.Image(rect, new Color32(224, 233, 247, 255), ui.Rounded).raycastTarget = false;
        var fill = Rect(rect, "Fill", 0, 0, 1, 1);
        ui.Image(fill, TaskUIBuilder.Blue, ui.Rounded).raycastTarget = false;
        var slider = rect.gameObject.AddComponent<Slider>(); slider.fillRect = fill; slider.interactable = false;
        slider.transition = Selectable.Transition.None; slider.navigation = new Navigation { mode = Navigation.Mode.None };
        return slider;
    }
    private static void BuildCharacter(Transform card)
    {
        foreach (Transform child in card.Cast<Transform>().ToArray())
            if (child.name != "background" && child.name != "Image" && !child.GetComponent<CharacterHomePreview>()) UnityEngine.Object.DestroyImmediate(child.gameObject);
        var image = card.Find("Image")?.GetComponent<Image>();
        if (!image) image = ui.Image(Rect(card, "Image", .06f, .43f, .94f, .88f), Color.white);
        var stage = Rect(card, "EquipmentStage", .04f, .41f, .96f, .89f);
        ui.Image(stage, new Color32(227, 229, 246, 255), ui.Rounded).raycastTarget = false;
        stage.SetSiblingIndex(1);
        Place(image.rectTransform, .06f, .43f, .94f, .88f); image.preserveAspect = true; image.raycastTarget = false;
        DashboardCharacterSetup.ConfigureCharacter(card);
        var status = card.GetComponent<CatStatusUI>() ?? card.gameObject.AddComponent<CatStatusUI>();
        status.playerName = Label(card, "Player", 25, .05f, .91f, .95f, .98f, true);
        status.playerName.alignment = TextAlignmentOptions.Center;
        var levelInfo = CopyPart(userInfo.Find("Popup/Level_Info"), card, "LevelInfo", .06f, .12f, .94f, .21f);
        status.level = levelInfo.Find("Text").GetComponent<TMP_Text>();
        Place(status.level.rectTransform, 0, 0, .24f, 1);
        StyleText(status.level, "LV 1", 28, TextAlignmentOptions.MidlineLeft);
        status.experienceBar = levelInfo.Find("Slider_Level").GetComponent<Slider>();
        Place((RectTransform)status.experienceBar.transform, .27f, .10f, 1, .90f);
        Place(status.experienceBar.fillRect, 0, 0, 1, 1);
        status.experienceBar.interactable = false; status.experienceBar.transition = Selectable.Transition.None;
        status.experienceBar.onValueChanged = new Slider.SliderEvent();
        status.experience = status.experienceBar.transform.Find("Text (TMP)").GetComponent<TMP_Text>();
        Place(status.experience.rectTransform, 0, 0, 1, 1);
        StyleText(status.experience, "0 / 100", 21, TextAlignmentOptions.Center);
        string[] labels = { "Weapon", "Outfit", "Accessory" };
        string[] sources = { "ItemSlot_Weapon", "ItemSlot_Dress", "ItemSlot_Helmet" };
        for (int i = 0; i < 3; i++)
        {
            float left = .18f + i * .23f;
            float bottom = .24f;
            var slot = CopyPart(equipment.Find("Panel_Left/ItemSlot_Group/" + sources[i]), card, labels[i], left, bottom, left + .18f, bottom + .14f);
            var glyph = slot.GetComponentInChildren<Image>(true);
            var childGlyph = slot.GetComponentsInChildren<Image>(true).First(g => g.transform != slot);
            Place(childGlyph.rectTransform, .20f, .20f, .80f, .80f);
            childGlyph.preserveAspect = true;
            if (i == 0)
            {
                glyph.sprite = equipment.Find("Panel_Left/ItemSlot_Group/ItemSlot_Helmet").GetComponent<Image>().sprite;
                childGlyph.sprite = equipment.Find("Right_Panel/TabMenu/Button_Weapon").GetComponent<Image>().sprite;
                childGlyph.color = equipment.Find("Panel_Left/ItemSlot_Group/ItemSlot_Helmet/Icon").GetComponent<Image>().color;
            }
        }
    }
    private static void BuildGoals(Transform card)
    {
        KeepBackground(card);
        card.Find("background").GetComponent<Image>().color = Color.white;
        var ribbon = CopyPart(mission.Find("Panel_Right/TableFrame/Label_Ribbon"), card, "DailyRewardRibbon", .015f, .83f, .985f, .94f);
        var title = ribbon.Find("Text_Title").GetComponent<TMP_Text>();
        Place(title.rectTransform, .18f, .30f, .82f, .95f);
        title.margin = Vector4.zero;
        StyleText(title, "Daily Mission Rewards", 25, TextAlignmentOptions.Center);
        Place((RectTransform)ribbon.Find("Icon"), .11f, .48f, .15f, .77f);
        Place((RectTransform)ribbon.Find("Icon (1)"), .85f, .48f, .89f, .77f);
        BuildGoal(card, DailyRewardGoal.ThreeQuests, "Complete 3 Quests", "Daily Tasks and Todo both count.", "Itemicon_Scroll_0", .45f, .78f);
        BuildGoal(card, DailyRewardGoal.Focus25Minutes, "Focus for 25 Minutes", "Total Focus time today: 25 min.", "Itemicon_Watch_Stopwatch", .07f, .40f);
    }
    private static void BuildGoal(Transform parent, DailyRewardGoal goal, string title, string description, string icon, float bottom, float top)
    {
        var source = mission.Find("Panel_Left/ScrollRect/Content/List (1)");
        var card = CopyPart(source, parent, goal.ToString(), .045f, bottom, .955f, top);
        var frame = (RectTransform)card.Find("IconFrame"); Place(frame, .035f, .48f, .205f, .92f);
        var item = frame.Find("Icon").GetComponent<Image>();
        item.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath + icon + ".Png");
        Place(item.rectTransform, .12f, .12f, .88f, .88f); item.preserveAspect = true;
        var heading = card.Find("Text_Info").GetComponent<TMP_Text>();
        Place(heading.rectTransform, .25f, .73f, .96f, .94f); StyleText(heading, title, 23, TextAlignmentOptions.MidlineLeft);
        Label(card, description, 15, .25f, .62f, .96f, .73f);
        var controller = card.gameObject.AddComponent<DailyGoalUI>(); controller.goal = goal;
        controller.progressBar = card.Find("Slider").GetComponent<Slider>();
        Place((RectTransform)controller.progressBar.transform, .25f, .46f, .96f, .54f);
        Place((RectTransform)controller.progressBar.transform.Find("FillArea"), 0, 0, 1, 1);
        Place(controller.progressBar.fillRect, 0, 0, 1, 1);
        Place((RectTransform)controller.progressBar.transform.Find("FillArea/TickMark"), 0, 0, 1, 1);
        controller.progressBar.interactable = false; controller.progressBar.transition = Selectable.Transition.None;
        controller.progressBar.onValueChanged = new Slider.SliderEvent();
        controller.progress = controller.progressBar.transform.Find("Text_Value").GetComponent<TMP_Text>();
        controller.progress.transform.SetParent(card, false);
        Place(controller.progress.rectTransform, .65f, .54f, .96f, .66f);
        StyleText(controller.progress, goal == DailyRewardGoal.ThreeQuests ? "0 / 3" : "0 / 25", 20, TextAlignmentOptions.MidlineRight);
        controller.progressFill = controller.progressBar.fillRect.GetComponent<Image>();
        controller.inProgressFillSprite = source.Find("Slider/FillArea/Fill").GetComponent<Image>().sprite;
        controller.completedFillSprite = mission.Find("Panel_Left/ScrollRect/Content/List/Slider/FillArea/Fill").GetComponent<Image>().sprite;
        controller.rowBackground = card.GetComponent<Image>();
        controller.inProgressSprite = source.GetComponent<Image>().sprite;
        controller.completedSprite = mission.Find("Panel_Left/ScrollRect/Content/List").GetComponent<Image>().sprite;
        var xp = card.Find("Icon_Coin").GetComponent<Image>();
        Place(xp.rectTransform, .045f, .09f, .145f, .34f);
        xp.sprite = mission.Find("Panel_Right/TableFrame/Item_Group/Group (1)/ItemList_Blue/Item").GetComponent<Image>().sprite;
        xp.preserveAspect = true;
        var xpCount = xp.transform.Find("Text_Num").GetComponent<TMP_Text>();
        xpCount.transform.SetParent(card, false); Place(xpCount.rectTransform, .045f, .025f, .145f, .12f);
        StyleText(xpCount, "20", 20, TextAlignmentOptions.Center);
        var fish = ui.Image(Rect(card, "Fish", .22f, .09f, .32f, .34f), Color.white,
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Generated/Icons/FishIcon_512_v2.png"));
        fish.type = Image.Type.Simple; fish.preserveAspect = true; fish.raycastTarget = false;
        Label(card, "10", 20, .22f, .025f, .32f, .12f, true).alignment = TextAlignmentOptions.Center;
        var inactive = (RectTransform)card.Find("Button_InProgress");
        Place(inactive, .42f, .07f, .96f, .34f);
        var inactiveText = inactive.GetComponentInChildren<TMP_Text>(true);
        Place(inactiveText.rectTransform, .03f, .06f, .97f, .94f);
        StyleText(inactiveText, "In Progress", 21, TextAlignmentOptions.Center);
        var claim = CopyPart(mission.Find("Panel_Left/ScrollRect/Content/List/Button_Claim"), card, "Claim", .42f, .07f, .96f, .34f);
        controller.claim = claim.GetComponent<Button>(); controller.claim.onClick = new Button.ButtonClickedEvent();
        controller.claimLabel = claim.GetComponentInChildren<TMP_Text>(true);
        Place(controller.claimLabel.rectTransform, .03f, .06f, .97f, .94f);
        StyleText(controller.claimLabel, "Claim", 23, TextAlignmentOptions.Center);
        controller.inProgress = inactive.gameObject; controller.inProgressLabel = inactiveText;
        controller.claim.gameObject.SetActive(false);
        controller.feedback = Label(card, "", 14, .36f, .005f, .97f, .09f);
    }
    private static RectTransform CopyPart(Transform source, Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        if (!source) throw new InvalidOperationException("Missing reference prefab part: " + name);
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent); clone.name = name;
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(behaviour is Graphic) && !(behaviour is Selectable) && !(behaviour is BaseMeshEffect) && !(behaviour is RectMask2D) && !(behaviour is Mask))
                UnityEngine.Object.DestroyImmediate(behaviour);
        var rect = (RectTransform)clone.transform; Place(rect, x0, y0, x1, y1);
        foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        foreach (var button in clone.GetComponentsInChildren<Button>(true))
            if (button.targetGraphic) button.targetGraphic.raycastTarget = true;
        return rect;
    }
    private static void StyleText(TMP_Text label, string text, float size, TextAlignmentOptions alignment)
    {
        label.text = text; label.alignment = alignment; label.enableAutoSizing = true;
        label.fontSizeMax = size; label.fontSizeMin = Mathf.Max(14, size - 4);
        label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
    }
    private static void BuildQuickFocus(Transform card, MainMenuNavigation navigation)
    {
        Icon(card, "Itemicon_Watch_Stopwatch", .04f, .18f, .18f, .83f);
        var panel = card.GetComponent<FocusPanelUI>() ?? card.gameObject.AddComponent<FocusPanelUI>();
        panel.quickStart = true; panel.navigation = navigation;
        panel.status = Label(card, "Focus Training", 24, .21f, .66f, .64f, .91f, true);
        panel.timer = Label(card, "25:00", 40, .21f, .23f, .56f, .66f, true);
        Label(card, "25 min Focus  /  5 min Rest", 17, .21f, .07f, .66f, .23f);
        panel.start = Button(card, "StartFocus", "Start Focus", .70f, .28f, .95f, .66f);
    }
    private static void BuildFocus(Transform page, MainMenuNavigation navigation)
    {
        KeepBackground(page);
        Label(page, "Focus", 32, .04f, .89f, .95f, .98f, true);
        var card = Rect(page, "FocusTraining", .16f, .07f, .84f, .85f);
        ui.Image(card, new Color32(245, 250, 255, 255), ui.Rounded).raycastTarget = false;
        var panel = page.GetComponent<FocusPanelUI>() ?? page.gameObject.AddComponent<FocusPanelUI>();
        panel.quickStart = false; panel.navigation = navigation;
        var character = UnityEngine.Object.Instantiate(navigation.pages[0].transform.Find("Top/Character/Image").gameObject, card);
        character.name = "TrainingCat";
        Place((RectTransform)character.transform, .08f, .37f, .36f, .80f);
        character.SetActive(true);
        panel.status = Label(card, "Focus Training", 30, .40f, .77f, .93f, .89f, true);
        panel.timer = Label(card, "25:00", 78, .40f, .50f, .93f, .76f, true);
        panel.progress = Bar(card, "Progress", .40f, .47f, .92f, .49f);
        Label(card, "25 minutes Focus  /  5 minutes Rest", 20, .40f, .36f, .94f, .44f);
        panel.message = Label(card, "25 minutes of Focus, then 5 minutes of Rest.", 20, .07f, .22f, .93f, .34f);
        panel.message.alignment = TextAlignmentOptions.Center;
        panel.start = Button(card, "StartFocus", "Start Focus", .30f, .10f, .70f, .19f);
        panel.endEarly = Button(card, "EndFocusEarly", "End Focus Early", .07f, .10f, .47f, .19f);
        panel.cancel = Button(card, "CancelFocus", "Cancel", .53f, .10f, .93f, .19f);
        panel.endRest = Button(card, "EndRestEarly", "End Rest Early", .30f, .10f, .70f, .19f);
        panel.retry = Button(card, "Retry", "Retry Save", .30f, .10f, .70f, .19f);
        panel.dashboard = Button(page, "ReturnToDashboard", "Return to Dashboard", .72f, .90f, .95f, .97f);
        Label(card, "A full session earns up to 25 EXP, 10 Fish Coins and 1 Ticket.\nThe daily Focus goal has its own Claim reward.", 18, .07f, .01f, .93f, .09f).alignment = TextAlignmentOptions.Center;
        panel.endEarly.gameObject.SetActive(false); panel.cancel.gameObject.SetActive(false);
        panel.endRest.gameObject.SetActive(false); panel.retry.gameObject.SetActive(false);
        FocusPageSetup.ConfigurePage(navigation.transform.root.gameObject);
    }
}
