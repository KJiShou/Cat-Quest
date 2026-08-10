using UnityEngine;

public class StoryCharacter : MonoBehaviour
{
    [Header("Character Info")]
    [SerializeField] private string displayName;

    [Header("Components")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer[] renderers;

    [Header("Default Animations")]
    [SerializeField] private string idleAnimation = "ninja-idle";
    [SerializeField] private string walkAnimation = "ninja-walk";
    [SerializeField] private string jumpUpAnimation = "ninja-jump A";
    [SerializeField] private string jumpDownAnimation = "ninja-jump B";

    public string DisplayName => displayName;

    public Animator Animator => animator;

    public string IdleAnimation => idleAnimation;
    public string WalkAnimation => walkAnimation;
    public string JumpUpAnimation => jumpUpAnimation;
    public string JumpDownAnimation => jumpDownAnimation;

    public void PlayAnimation(string animationName)
    {
        if (animator == null ||
            string.IsNullOrEmpty(animationName))
        {
            return;
        }

        animator.Play(animationName, 0, 0f);
    }

    public void SetVisualColor(Color color)
    {
        foreach (SpriteRenderer spriteRenderer in renderers)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = color;
            }
        }
    }
}