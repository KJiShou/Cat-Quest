using UnityEngine;
using System.IO;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    public PlayerData PlayerData { get; private set; }

    private string savePath;

    [ContextMenu("Delete Save Data")]
    public void DeleteSaveData()
    {
        string savePath = Path.Combine(
            Application.persistentDataPath,
            "catquest-data.json"
        );

        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log("Save data deleted.");
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        savePath = Path.Combine(
            Application.persistentDataPath,
            "catquest-data.json"
        );

        LoadData();
    }

    private void LoadData()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);

            PlayerData = JsonUtility.FromJson<PlayerData>(json);
        }
        else
        {
            PlayerData = new PlayerData();
        }
    }

    public void SetUsername(string username)
    {
        PlayerData.username = username;
        SaveData();
    }

    public string GetUsername()
    {
        return PlayerData.username;
    }

    public bool HasUsername()
    {
        return !string.IsNullOrWhiteSpace(PlayerData.username);
    }

    public void SaveData()
    {
        string json = JsonUtility.ToJson(PlayerData, true);

        File.WriteAllText(savePath, json);

        Debug.Log("Game saved to: " + savePath);
    }
}