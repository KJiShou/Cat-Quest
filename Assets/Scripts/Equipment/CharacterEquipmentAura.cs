using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

// Shared presentation only. EquipmentRules remains the authority for valid worn pieces.
[DefaultExecutionOrder(200)]
public sealed class CharacterEquipmentAura : MonoBehaviour
{
    [Tooltip("Blue 1/2/3, purple 1/2/3, yellow 1/2/3.")]
    public GameObject[] circlePrefabs = new GameObject[9];
    [FormerlySerializedAs("footOffset")]
    public Vector3 characterOffset = new Vector3(-.9f, -.55f, 0);
    public Vector3 circleScale = Vector3.one * 3.5f;
    public float threePieceLift;
    SpriteRenderer character;
    TaskService service;
    GameObject circle;
    SortingGroup ownedSortingGroup;
    bool remote;
    public int Tier { get; private set; }
    public int Pieces { get; private set; }
    public GameObject Circle => circle;

    public static void Select(int[] counts, out int tier, out int pieces)
    {
        tier = pieces = 0;
        if (counts == null) return;
        for (int i = 0; i < Mathf.Min(3, counts.Length); i++)
            if (counts[i] > 0 && counts[i] <= 3 && counts[i] >= pieces)
            { tier = i + 1; pieces = counts[i]; }
    }

    void Awake() { character = GetComponentInParent<SpriteRenderer>(); }
    void OnEnable() { Connect(); }
    void Update() { if (!remote && service == null) Connect(); }
    void Connect()
    {
        if (remote || service != null || !GameDataManager.Instance) return;
        service = GameDataManager.Instance.Tasks;
        if (service == null) return;
        service.Changed += Refresh;
        Refresh();
    }
    void Refresh()
    {
        Select(service.EquipmentStats.SetPieces, out int tier, out int pieces);
        Show(tier, pieces);
    }
    public void SetRemote(int tier, int pieces)
    {
        remote = true;
        Disconnect();
        Show(tier, pieces);
    }
    void Show(int tier, int pieces)
    {
        if (tier < 1 || tier > 3 || pieces < 1 || pieces > 3) tier = pieces = 0;
        if (Tier == tier && Pieces == pieces && (tier == 0 || circle)) return;
        Clear(); Tier = tier; Pieces = pieces;
        int index = (tier - 1) * 3 + pieces - 1;
        if (tier == 0 || circlePrefabs == null || index >= circlePrefabs.Length || !circlePrefabs[index]) return;
        // Sort the character and its circle together against the environment,
        // then put the circle behind the sprite inside that group.
        if (character && !character.GetComponent<SortingGroup>())
            ownedSortingGroup = character.gameObject.AddComponent<SortingGroup>();
        if (ownedSortingGroup)
        {
            ownedSortingGroup.sortingLayerID = character.sortingLayerID;
            ownedSortingGroup.sortingOrder = character.sortingOrder + 1;
            ownedSortingGroup.enabled = true;
        }
        circle = Instantiate(circlePrefabs[index], transform);
        circle.name = "Equipment Circle " + tier + "-" + pieces;
        circle.transform.localPosition = pieces == 3 ? Vector3.up * threePieceLift : Vector3.zero;
        circle.transform.localRotation = Quaternion.identity;
        circle.transform.localScale = circleScale;
        // The source effect includes a demo equipment icon, not part of the aura.
        var demoIcon = circle.transform.Find("Demo_Item_Icon_3");
        if (demoIcon) demoIcon.gameObject.SetActive(false);
        foreach (var ps in circle.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.useUnscaledTime = true;
            // Vendor particles launch along Z at speed 5. A perspective arena
            // turns that depth travel into drifting/scaling even while the cat idles.
            main.startSpeed = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;
            main.loop = true;
            ps.Play(false);
        }
        foreach (var renderer in circle.GetComponentsInChildren<ParticleSystemRenderer>())
        {
            renderer.gameObject.layer = character ? character.gameObject.layer : gameObject.layer;
            if (character) { renderer.sortingLayerID = character.sortingLayerID; renderer.sortingOrder = character.sortingOrder - 1; }
        }
    }
    void LateUpdate()
    {
        // Fixed body anchor avoids bobbing as padded animation frames change.
        transform.localPosition = characterOffset;
    }
    void Disconnect() { if (service != null) service.Changed -= Refresh; service = null; }
    void Clear() { if (circle) { circle.SetActive(false); Destroy(circle); } circle = null; if (ownedSortingGroup) ownedSortingGroup.enabled = false; }
    void OnDisable() { Disconnect(); Clear(); Tier = Pieces = 0; }
    void OnDestroy() { Disconnect(); if (ownedSortingGroup) Destroy(ownedSortingGroup); }
}
