using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Presentation only: observes committed balances; never grants or saves rewards.
[DefaultExecutionOrder(1000)]
public sealed class RewardFlightUI : MonoBehaviour
{
    public Image iconTemplate;
    public TMP_Text amountTemplate;
    public Image fishTarget, ticketTarget;
    public CatStatusUI catStatus;
    public TMP_Text fishValue, ticketValue;
    public Sprite experienceIcon;
    const int Capacity = 48;
    const float StackDuration = 1.2f;
    sealed class Flight
    {
        public Image icon;
        public int kind, amount;
        public float age, delay;
        public Vector2 origin, scatter;
        public bool active;
    }
    readonly Flight[] flights = new Flight[Capacity];
    readonly int[] observed = new int[3], displayed = new int[3], pending = new int[3];
    readonly float[] pulse = new float[3];
    readonly long[] stacked = new long[3];
    readonly JuicyButtonMesh[] targetFeedback = new JuicyButtonMesh[2];
    readonly TMP_Text[] gains = new TMP_Text[3];
    TaskService service;
    RectTransform surface;
    Canvas canvas;
    Vector2 pointerOrigin;
    float pointerTime = -10;
    bool initialized;
    RectTransform rewardOrigin;
    public int ActiveCount { get; private set; }

    // Scoped by the caller around settlement; only committed balance changes fly.
    public void SetRewardOrigin(RectTransform origin) => rewardOrigin = origin;

    void Start()
    {
        surface = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>().rootCanvas;
        for (int i = 0; i < Capacity; i++)
        {
            var icon = Instantiate(iconTemplate, transform);
            icon.name = "Reward Icon"; icon.raycastTarget = false;
            flights[i] = new Flight { icon = icon };
        }
        for (int i = 0; i < 3; i++)
        {
            gains[i] = Instantiate(amountTemplate, transform);
            gains[i].raycastTarget = false;
        }
        for (int i = 0; i < 2; i++)
        {
            var target = Target(i);
            targetFeedback[i] = target.GetComponent<JuicyButtonMesh>() ?? target.gameObject.AddComponent<JuicyButtonMesh>();
        }
        initialized = true;
        Subscribe();
    }
    void OnEnable()
    {
        // Unity restores private scalar fields during a Play Mode script reload,
        // but not the non-serializable flight pool. Rebuild only that transient UI.
        if (initialized && flights[0] == null)
        {
            initialized = false;
            foreach (Transform child in transform)
                if (child.name == "Reward Icon" || child.name == amountTemplate.name + "(Clone)")
                    Destroy(child.gameObject);
            Start();
        }
        else if (initialized) Subscribe();
    }
    void Subscribe()
    {
        service = GameDataManager.EnsureInstance().Tasks;
        Sync(); service.Changed += Changed;
    }
    void OnDisable()
    {
        if (service != null) service.Changed -= Changed;
        if (initialized && flights[0] != null) { Sync(); PaintValues(); }
    }
    void OnApplicationFocus(bool focused) { if (!focused && initialized && flights[0] != null) { Sync(); PaintValues(); } }
    void Sync()
    {
        var data = service.Snapshot;
        observed[0] = displayed[0] = data.fishCoins;
        observed[1] = displayed[1] = data.tickets;
        observed[2] = displayed[2] = data.experience;
        Array.Clear(pending, 0, 3); Array.Clear(pulse, 0, 3); Array.Clear(stacked, 0, 3);
        foreach (var feedback in targetFeedback) if (feedback) feedback.ResetRewardPulse();
        foreach (var f in flights) { f.active = false; f.icon.gameObject.SetActive(false); }
        foreach (var gain in gains) gain.gameObject.SetActive(false);
        ActiveCount = 0;
    }
    Vector2 Local(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(surface, screen,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var point);
        return point;
    }
    RectTransform Target(int kind) => kind == 0 ? fishTarget.rectTransform : kind == 1 ? ticketTarget.rectTransform : catStatus.experienceBar.GetComponent<RectTransform>();
    Vector2 Destination(int kind) => Local(RectTransformUtility.WorldToScreenPoint(
        canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, Target(kind).TransformPoint(Target(kind).rect.center)));
    Sprite Icon(int kind) => kind == 0 ? fishTarget.sprite : kind == 1 ? ticketTarget.sprite : experienceIcon;
    void Changed()
    {
        var data = service.Snapshot;
        int[] totals = { data.fishCoins, data.tickets, data.experience };
        var origin = Time.unscaledTime - pointerTime < .8f ? pointerOrigin : surface.rect.center + new Vector2(0, -40);
        if (rewardOrigin)
            origin = Local(RectTransformUtility.WorldToScreenPoint(
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rewardOrigin.TransformPoint(rewardOrigin.rect.center)));
        for (int kind = 0; kind < 3; kind++)
        {
            int delta = totals[kind] - observed[kind];
            observed[kind] = totals[kind];
            if (delta <= 0) { displayed[kind] = Math.Max(0, totals[kind] - pending[kind]); continue; }
            int count = Math.Min(6, delta);
            for (int i = 0; i < count; i++)
            {
                int amount = delta / count + (i < delta % count ? 1 : 0);
                Flight free = null;
                foreach (var f in flights) if (!f.active) { free = f; break; }
                if (free == null) { displayed[kind] += amount; RecordArrival(kind, amount); continue; }
                free.active = true; free.kind = kind; free.amount = amount; free.age = 0;
                free.delay = i * .055f + kind * .09f;
                free.origin = origin + (rewardOrigin ? Vector2.zero : new Vector2((kind - 1) * 55, 0));
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2);
                free.scatter = free.origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(45f, 105f);
                free.scatter.x = Mathf.Clamp(free.scatter.x, surface.rect.xMin + 28, surface.rect.xMax - 28);
                free.scatter.y = Mathf.Clamp(free.scatter.y, surface.rect.yMin + 28, surface.rect.yMax - 28);
                free.icon.sprite = Icon(kind); free.icon.gameObject.SetActive(false);
                pending[kind] += amount; ActiveCount++;
            }
        }
    }
    void RecordArrival(int kind, int amount)
    {
        if (pulse[kind] <= 0) stacked[kind] = 0;
        stacked[kind] += amount;
        gains[kind].text = "+" + stacked[kind];
        pulse[kind] = StackDuration;
        if (kind < 2) targetFeedback[kind].PlayRewardPulse();
    }
    void Update()
    {
        if (!initialized) return;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        { pointerOrigin = Local(Mouse.current.position.ReadValue()); pointerTime = Time.unscaledTime; }
        float dt = Time.unscaledDeltaTime;
        foreach (var f in flights)
        {
            if (!f.active) continue;
            f.age += dt;
            float t = f.age - f.delay;
            if (t < 0) continue;
            f.icon.gameObject.SetActive(true);
            Vector2 end = Destination(f.kind);
            if (t < .3f)
            {
                float u = t / .3f;
                f.icon.rectTransform.anchoredPosition = Vector2.Lerp(f.origin, f.scatter, 1 - Mathf.Pow(1 - u, 3));
                f.icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(.3f, 1.15f, Mathf.Sin(u * Mathf.PI * .5f));
            }
            else
            {
                float u = Mathf.Clamp01((t - .3f) / .65f); float eased = u * u;
                Vector2 control = (f.scatter + end) * .5f + new Vector2(0, 90);
                f.icon.rectTransform.anchoredPosition = (1-eased)*(1-eased)*f.scatter + 2*(1-eased)*eased*control + eased*eased*end;
                f.icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.15f, .55f, u);
                if (u >= 1)
                {
                    pending[f.kind] -= f.amount;
                    displayed[f.kind] = Math.Max(0, observed[f.kind] - pending[f.kind]);
                    RecordArrival(f.kind, f.amount);
                    f.active = false; f.icon.gameObject.SetActive(false); ActiveCount--;
                }
            }
        }
        for (int i = 0; i < 3; i++)
        {
            pulse[i] = Mathf.Max(0, pulse[i] - dt);
            var gain = gains[i]; gain.gameObject.SetActive(pulse[i] > 0);
            if (pulse[i] <= 0) continue;
            float u = 1 - pulse[i] / StackDuration;
            float fade = Mathf.Clamp01((u - .65f) / .35f);
            gain.rectTransform.anchoredPosition = Destination(i) + new Vector2(45, -35 - fade * 18);
            gain.rectTransform.localScale = Vector3.one * (1 + .22f * Mathf.Sin(Mathf.Clamp01(u * 5) * Mathf.PI));
            gain.color = new Color(0f, .6f, 1f, 1-fade);
        }
    }
    void LateUpdate() { if (initialized) PaintValues(); }
    void PaintValues()
    {
        if (!fishValue || !ticketValue || !catStatus) return;
        fishValue.text = displayed[0].ToString(); ticketValue.text = displayed[1].ToString();
        TaskService.CatLevel(displayed[2], out int level, out int xp, out int required);
        catStatus.level.text = "LV " + level; catStatus.experience.text = $"{xp} / {required}";
        catStatus.experienceBar.maxValue = required; catStatus.experienceBar.SetValueWithoutNotify(xp);
    }
}
