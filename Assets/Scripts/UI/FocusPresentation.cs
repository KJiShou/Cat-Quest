// Display-only state shared by the main page and the companion. Never owns rewards.
public interface IFocusPresentation
{
    FocusPhase Phase { get; }
    int SpectatorFishCount { get; }
}
