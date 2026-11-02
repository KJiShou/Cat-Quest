using TMPro;
using UnityEngine;
using System.Linq;

// Keep the authored controls even when a bind temporarily hides the tick or tag chip.
internal sealed class TaskRowBindings : MonoBehaviour
{
    public TMP_Text title, schedule, fish, experience;
    public Transform tick, tags, difficulty;
    static Transform AuthoredControl(Transform row, string name)
    {
        return TaskUIBuilder.Find(row,name) ?? row.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
    }
    public static TaskRowBindings For(GameObject row)
    {
        if (row.TryGetComponent<TaskRowBindings>(out var cached)) return cached;
        cached = row.AddComponent<TaskRowBindings>();
        cached.title = TaskUIBuilder.Find(row.transform,"TaskName").GetComponent<TMP_Text>();
        cached.schedule = TaskUIBuilder.Find(row.transform,"DateTime").GetComponent<TMP_Text>();
        cached.tick = AuthoredControl(row.transform,"Tick");
        cached.tags = AuthoredControl(row.transform,"Tags");
        cached.difficulty = TaskUIBuilder.Find(row.transform,"Normal_Difficulty");
        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true)) {
            if (text.name == "FishNum") cached.fish = text;
            else if (text.name == "FishNum (1)") cached.experience = text;
        }
        return cached;
    }
}
