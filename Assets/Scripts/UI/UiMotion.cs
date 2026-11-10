using UnityEngine;

// Shared presentation preference; reading it never copies a save or creates a service.
public static class UiMotion
{
    static bool companionReduced;
    public static bool Reduced => GameDataManager.Instance
        ? GameDataManager.Instance.Tasks != null && GameDataManager.Instance.Tasks.ReduceMotion
        : companionReduced;
    public static void ApplyCompanion(bool reduced) => companionReduced = reduced;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => companionReduced = false;
}
