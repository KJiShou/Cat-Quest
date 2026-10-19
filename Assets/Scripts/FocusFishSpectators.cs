using System.Collections.Generic;
using UnityEngine;

// Displays this session's reward progress without granting currency or creating a session.
public sealed class FocusFishSpectators : MonoBehaviour
{
    public Transform ninja;
    [Tooltip("Fish and configured bubbles, authored together in Unity.")]
    public GameObject fishPrefab;
    [Tooltip("Only used when no FocusRunner exists, for standalone scene previews.")]
    [Min(0)] public float previewElapsedSeconds = 1500;
    [InspectorName("Audience Center")] public Vector2 firstRowCenter = new Vector2(-2, -6.5f);
    public Vector2 spawnAreaSize = new Vector2(12, 1.8f);
    [Min(0.1f)] public float minimumSeparation = 1.2f;
    float FishWidth => fishPrefab ? fishPrefab.GetComponentInChildren<SpriteRenderer>().sprite.bounds.size.x * Mathf.Abs(fishPrefab.transform.localScale.x) : 1.05f;
    public Camera viewportCamera;
    [Min(.1f)] public float swimInDuration = 1.8f;
    public int VisibleFishCount => fish.Count;

    readonly List<SpriteRenderer> fish = new List<SpriteRenderer>();
    readonly List<Vector2> positions = new List<Vector2>();
    readonly List<Vector2> entryPositions = new List<Vector2>();
    readonly List<float> entryTimes = new List<float>();
    FocusRunner runner;
    public IFocusPresentation Presentation { get; set; }
    void Update()
    {
        if (Presentation == null && !runner) runner = FindFirstObjectByType<FocusRunner>();
        int count = Presentation != null ? Presentation.SpectatorFishCount : runner ? runner.Controller.SpectatorFishCount : TaskService.FocusFishForSeconds(previewElapsedSeconds);
        SetFishCount(count);
        AnimateFish(Time.time);
    }

    public void SetFishCount(int count)
    {
        count = Mathf.Max(0, count);
        if (!fishPrefab || !ninja) return;
        while (fish.Count > count)
        {
            int last = fish.Count - 1;
            fish[last].transform.parent.gameObject.SetActive(false);
            Destroy(fish[last].transform.parent.gameObject);
            fish.RemoveAt(last);
            positions.RemoveAt(last);
            entryPositions.RemoveAt(last);
            entryTimes.RemoveAt(last);
        }
        while (fish.Count < count)
        {
            var go = Instantiate(fishPrefab, transform);
            var sprite = go.GetComponentInChildren<SpriteRenderer>();
            go.name = "Spectator Fish " + (fish.Count + 1);
            fish.Add(sprite);
            positions.Add(ChoosePosition());
            entryPositions.Add(ChooseEntry(positions[positions.Count - 1]));
            entryTimes.Add(Time.time);
            go.transform.localPosition = entryPositions[entryPositions.Count - 1];
        }
    }

    Vector2 ChooseEntry(Vector2 destination)
    {
        var camera = viewportCamera ? viewportCamera : Camera.main;
        float left = firstRowCenter.x - Mathf.Abs(spawnAreaSize.x) * .5f - FishWidth * 2;
        float right = firstRowCenter.x + Mathf.Abs(spawnAreaSize.x) * .5f + FishWidth * 2;
        if (camera && camera.orthographic)
        {
            float depth = Mathf.Abs(camera.transform.position.z - transform.position.z);
            left = transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0, .5f, depth))).x - FishWidth;
            right = transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(1, .5f, depth))).x + FishWidth;
        }
        return new Vector2(Mathf.Abs(destination.x - left) < Mathf.Abs(destination.x - right) ? left : right, destination.y);
    }

    Vector2 ChoosePosition()
    {
        Vector2 best = firstRowCenter;
        float bestDistance = -1;
        // Keep a fish's position for its lifetime; new arrivals never move the existing audience.
        for (int attempt = 0; attempt < 128; attempt++)
        {
            Vector2 candidate = firstRowCenter + new Vector2(
                Random.Range(-Mathf.Abs(spawnAreaSize.x) * .5f, Mathf.Abs(spawnAreaSize.x) * .5f),
                Random.Range(-Mathf.Abs(spawnAreaSize.y) * .5f, Mathf.Abs(spawnAreaSize.y) * .5f));
            float distance = float.PositiveInfinity;
            foreach (var position in positions) distance = Mathf.Min(distance, Vector2.Distance(candidate, position));
            if (distance >= minimumSeparation) return candidate;
            if (distance > bestDistance) { best = candidate; bestDistance = distance; }
        }
        return best;
    }

    public void AnimateFish(float time)
    {
        for (int i = 0; i < fish.Count; i++)
        {
            float bob = Mathf.Sin(time * 1.5f + i * 1.3f) * .09f;
            var sprite = fish[i];
            float progress = Mathf.Clamp01((time - entryTimes[i]) / Mathf.Max(.1f, swimInDuration));
            float ease = 1 - Mathf.Pow(1 - progress, 3);
            Vector2 location = Vector2.Lerp(entryPositions[i], positions[i], ease);
            sprite.transform.parent.localPosition = new Vector3(location.x, location.y + bob, 0);
            Vector3 direction = progress < 1 ? transform.TransformPoint(positions[i]) - sprite.transform.position
                : ninja.position - sprite.transform.position;
            sprite.flipX = direction.x < 0; // Supplied icon faces right.
            float tilt = progress < 1 ? 0 : Mathf.Clamp(Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg * .25f, 0, 22);
            sprite.transform.localRotation = Quaternion.Euler(0, 0, sprite.flipX ? -tilt : tilt);
        }
    }
}
