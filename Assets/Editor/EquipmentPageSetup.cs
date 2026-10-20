using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class EquipmentPageSetup
{
    static EquipmentPageSetup(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/setup-equipment.request"))return;File.Delete("Temp/setup-equipment.request");try{Build();File.WriteAllText("Temp/setup-equipment.result","PASS");}catch(Exception e){File.WriteAllText("Temp/setup-equipment.result",e.ToString());Debug.LogException(e);}}
    [MenuItem("Tools/Cat Quest/Build Equipment Page")]
    public static void Build(){
        var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try{
            var nav=root.GetComponentInChildren<MainMenuNavigation>(true);var page=nav.pages[(int)MainMenuPage.CatHome];
            foreach(Transform child in page.transform.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach(var layout in page.GetComponents<LayoutGroup>())UnityEngine.Object.DestroyImmediate(layout);
            var component=page.GetComponent<EquipmentPageUI>()??page.AddComponent<EquipmentPageUI>();component.font=nav.taskSystem.font;component.rounded=nav.taskSystem.roundedSprite;component.navigation=nav;
            const string buttons="Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Button/Button_Demo_Common/";
            component.blueButton=AssetDatabase.LoadAssetAtPath<Sprite>(buttons+"Btn_Rectangle00_n_Blue.png");component.redButton=AssetDatabase.LoadAssetAtPath<Sprite>(buttons+"Btn_Rectangle00_n_Red.png");
            const string icons="Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/512/";
            component.slotIcons=new[]{"Itemicon_Equipment_Sword.Png","Itemicon_Equipment_Armor.Png","Itemicon_Equipment_Helmet.Png"}.Select(n=>AssetDatabase.LoadAssetAtPath<Sprite>(icons+n)).ToArray();
            if(component.slotIcons.Any(x=>!x))throw new Exception("Equipment icon missing");
            component.catPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/Character/Spine 2D Ninja Cats Character Set/Characters/Prefab/Ninja Sprites.prefab");
            component.catPoster=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Generated/Previews/CharacterHomeDashboard.png");
            component.gradeSheets=new[]{"Blue","Purple","Yellow"}.Select(c=>AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CartoonVFX9X/BundleSpriteSheetEffect/Textures/Square_Sheet_1_"+c+".png")).ToArray();
            const string pictos="Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_PictoIcons/128/";
            component.statIcons=new[]{"PictoIcon_Life.Png","PictoIcon_Attack.Png","PictoIcon_Shield.Png","PictoIcon_Speed.Png"}.Select(n=>AssetDatabase.LoadAssetAtPath<Sprite>(pictos+n)).ToArray();
            var reference=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/UI/Layer Lab/GUI Pro-SimpleCasual/Prefabs/Prefabs_DemoScene_Panels/Light/Equipment.prefab").transform.Find("Right_Panel/TabMenu");
            component.tabIcons=new[]{"Button_All","Button_Helmet","Button_Weapon","Button_Shield"}.Select(n=>reference.Find(n).GetComponent<Image>().sprite).ToArray();
            component.tabIcons[3]=AssetDatabase.LoadAssetAtPath<Sprite>(pictos+"PictoIcon_Armor.Png");
            component.tabFocus=reference.Find("Button_All/TabFocus").GetComponent<Image>().sprite;
            var rewards=nav.pages[(int)MainMenuPage.Boss].GetComponent<EquipmentPageUI>()??nav.pages[(int)MainMenuPage.Boss].AddComponent<EquipmentPageUI>();
            EditorUtility.CopySerialized(component,rewards);rewards.claimsOnly=true;component.claimsOnly=false;
            var card=nav.pages[0].transform.Find("Top/Character");
            var mini=card.GetComponent<DashboardEquipmentUI>()??card.gameObject.AddComponent<DashboardEquipmentUI>();
            mini.navigation=nav;mini.gradeSheets=component.gradeSheets;mini.slots=new Button[3];mini.icons=new Image[3];
            string[] names={"Weapon","Outfit","Accessory"};
            for(int i=0;i<3;i++){
                var slot=card.Find(names[i]);mini.slots[i]=slot.GetComponent<Button>()??slot.gameObject.AddComponent<Button>();
                mini.slots[i].targetGraphic=slot.GetComponent<Image>();mini.slots[i].targetGraphic.raycastTarget=true;
                mini.icons[i]=slot.GetComponentsInChildren<Image>(true).First(x=>x.transform!=slot);mini.icons[i].sprite=component.slotIcons[i];mini.icons[i].color=Color.white;
            }
            var boss=nav.pages[(int)MainMenuPage.Boss].GetComponent<BossPageUI>();
        boss.actors.weaponSlashPrefabs=new[]{36,37,35}.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CartoonVFX9X/BundleSpriteSheetEffect/Prefabs/Slash_FX_"+n+".prefab")).ToArray();
            boss.masteryChestClosed=AssetDatabase.LoadAssetAtPath<Sprite>(icons+"Itemicon_Chest_Blue.Png");
            boss.masteryChestOpen=AssetDatabase.LoadAssetAtPath<Sprite>(icons+"Itemicon_Chest_Blue_Open.Png");
            boss.masteryIcons=boss.masteryBars.Select(bar=>bar.transform.parent.Find("ArmorBadge/ArmorIcon").GetComponent<Image>()).ToArray();
            foreach(var icon in boss.masteryIcons){icon.sprite=boss.masteryChestClosed;icon.preserveAspect=true;icon.raycastTarget=false;}
            foreach(var bar in boss.masteryBars){var image=bar.transform.parent.GetComponent<Image>();image.raycastTarget=true;var button=image.GetComponent<Button>()??image.gameObject.AddComponent<Button>();button.targetGraphic=image;}
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");
        }finally{PrefabUtility.UnloadPrefabContents(root);}AssetDatabase.SaveAssets();
    }
}
