using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
static class CenterUpgradePrice
{
    const string Request = "Temp/center-upgrade-price.request";
    const string Prefab = "Assets/Prefabs/EquipmentBulkUpgrade.prefab";
    static CenterUpgradePrice() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        try {
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try {
                var fields = new SerializedObject(root.GetComponent<EquipmentBulkUpgradeUI>());
                var button = (Button)fields.FindProperty("confirm").objectReferenceValue;
                var price = (TMP_Text)fields.FindProperty("price").objectReferenceValue;
                var fish = (RectTransform)button.transform.Find("FishCostIcon");
                fish.SetAsFirstSibling();
                var layout = button.GetComponent<HorizontalLayoutGroup>();
                if (!layout) layout = button.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.padding = new RectOffset(20,20,0,0);
                layout.spacing = 12;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                var iconSize = fish.GetComponent<LayoutElement>();
                if (!iconSize) iconSize = fish.gameObject.AddComponent<LayoutElement>();
                iconSize.minWidth = iconSize.preferredWidth = fish.sizeDelta.x;
                price.alignment = TextAlignmentOptions.Center;
                PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            } finally { PrefabUtility.UnloadPrefabContents(root); }
            File.WriteAllText("Temp/center-upgrade-price.result","PASS");
        } catch(Exception e) { File.WriteAllText("Temp/center-upgrade-price.result",e.ToString()); }
    }
}
