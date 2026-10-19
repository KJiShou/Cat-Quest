using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class DashboardCharacterSetup {
    static DashboardCharacterSetup(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/setup-dashboard-character.request"))return;
        File.Delete("Temp/setup-dashboard-character.request");
        var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try{
            var nav=root.GetComponentInChildren<MainMenuNavigation>(true);
            var card=nav.pages[0].transform.Find("Top/Character");
            ConfigureCharacter(card);
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Canvas.prefab");
            File.WriteAllText("Library/FocusBattleVerification/dashboard-character-setup.txt","Dashboard independent character prefab assigned.");
        }finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    public static void ConfigureCharacter(Transform card){
            var preview=card.GetComponentInChildren<CharacterHomePreview>(true);
            if(!preview){
                var go=new GameObject("DashboardCharacterPreview",typeof(RectTransform),typeof(RawImage),typeof(CharacterHomePreview));
                go.transform.SetParent(card,false);preview=go.GetComponent<CharacterHomePreview>();
            }
            preview.characterPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Packages/Character/Spine 2D Ninja Cats Character Set/Characters/Prefab/Ninja Sprites.prefab");
            preview.showSceneBackground=true;
            var placeholder=card.Find("Image")?.GetComponent<Image>();
            if(!placeholder){
                var go=new GameObject("Image",typeof(RectTransform),typeof(Image));go.transform.SetParent(card,false);
                placeholder=go.GetComponent<Image>();
                placeholder.rectTransform.anchorMin=new Vector2(.06f,.43f);placeholder.rectTransform.anchorMax=new Vector2(.94f,.88f);
                placeholder.rectTransform.offsetMin=placeholder.rectTransform.offsetMax=Vector2.zero;go.transform.SetSiblingIndex(2);
            }
            placeholder.sprite=preview.characterPrefab.GetComponent<SpriteRenderer>().sprite;
            const string posterPath="Assets/Generated/Previews/CharacterHomeDashboard.png";
            if(File.Exists(posterPath)){
                AssetDatabase.ImportAsset(posterPath,ImportAssetOptions.ForceUpdate);
                var importer=(TextureImporter)AssetImporter.GetAtPath(posterPath);
                if(importer.textureType!=TextureImporterType.Sprite||importer.spriteImportMode!=SpriteImportMode.Single){
                    importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                    importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
                }
                placeholder.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(posterPath);
            }
            placeholder.enabled=true;placeholder.gameObject.SetActive(true);
            placeholder.color=Color.white;placeholder.preserveAspect=true;
            preview.GetComponent<RawImage>().enabled=false;
            var rect=(RectTransform)preview.transform;
            rect.anchorMin=placeholder.rectTransform.anchorMin;rect.anchorMax=placeholder.rectTransform.anchorMax;
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            rect.SetSiblingIndex(3);
    }
}
