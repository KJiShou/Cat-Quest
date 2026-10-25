using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Editor-only profile selection. Runtime initialization already honors this variable.
public static class ManualTestAccount
{
    internal const string StoreVariable = "CATQUEST_TASK_TEST_STORE";
    const string MenuRoot = "Tools/Cat Quest/Test Account/";
    internal static string SavePath => Path.GetFullPath("Library/ManualTestAccount/save.json");
    internal static bool CanSwitch => !EditorApplication.isPlayingOrWillChangePlaymode
        && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

    [MenuItem(MenuRoot + "Use Late Game Account")]
    public static void UseTestAccount()
    {
        if (!CanSwitch) return;
        try
        {
            EnsureSave(SavePath);
            Environment.SetEnvironmentVariable(StoreVariable, SavePath);
            Debug.Log("Late-game test account selected for the next Play session. Save: " + SavePath);
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    [MenuItem(MenuRoot + "Use Late Game Account", true)]
    static bool ValidateTest()
    {
        Menu.SetChecked(MenuRoot + "Use Late Game Account",
            string.Equals(Environment.GetEnvironmentVariable(StoreVariable), SavePath, StringComparison.OrdinalIgnoreCase));
        return CanSwitch;
    }

    [MenuItem(MenuRoot + "Use Normal Account")]
    public static void UseNormalAccount()
    {
        if (!CanSwitch) return;
        Environment.SetEnvironmentVariable(StoreVariable, null);
        Debug.Log("Normal account selected for the next Play session. Test progress is retained.");
    }

    [MenuItem(MenuRoot + "Use Normal Account", true)]
    static bool ValidateNormal()
    {
        Menu.SetChecked(MenuRoot + "Use Normal Account",
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(StoreVariable)));
        return CanSwitch;
    }

    [MenuItem(MenuRoot + "Show Test Save")]
    static void ShowSave()
    {
        if (File.Exists(SavePath)) EditorUtility.RevealInFinder(SavePath);
        else Debug.Log("Select Use Late Game Account to create the test save first.");
    }

    internal static void EnsureSave(string path)
    {
        // Never reset an existing profile, including its recoverable backup.
        if (File.Exists(path) || File.Exists(path + ".bak")) return;
        var data = new PlayerData
        {
            username = "TEST - Master Stage", experience = 13050,
            fishCoins = 10000, tickets = 100, equipmentUnlocked = true, masterStage = 1
        };
        for (int boss = 1; boss <= 3; boss++)
            data.bossProgress.Add(new BossProgress { bossId = boss, wins = 15 });
        foreach (var definition in EquipmentRules.Catalog)
        {
            data.equipment.Add(new EquipmentItem { id = definition.Id, level = 30 });
            if (definition.Tier == 3) EquipmentRules.SetEquipped(data, definition.Slot, definition.Id);
        }
        // Fresh local data: no copied tasks, account identity or upload preferences.
        var store = new PlayerDataStore(path);
        if (!store.TrySave(data, out string error)) throw new IOException(error);
    }
}
