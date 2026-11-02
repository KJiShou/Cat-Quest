using UnityEngine;

// Each entry lives separately in Resources so opening one arena loads only its dependencies.
public sealed class StagePrefabResource : ScriptableObject
{
    public GameObject prefab;
    public static GameObject Resolve(GameObject direct, string path)
    {
        if (direct || string.IsNullOrEmpty(path)) return direct;
        var entry = Resources.Load<StagePrefabResource>(path);
        if (entry && entry.prefab) return entry.prefab;
        Debug.LogError("Missing stage prefab resource " + path);
        return null;
    }
}
