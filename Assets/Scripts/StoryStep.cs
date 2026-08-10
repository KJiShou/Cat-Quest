using System;
using UnityEngine;

public enum StoryStepType
{
    Movement,
    Dialogue,
    Animation,
    Wait
}

public enum MovementType
{
    Walk,
    Jump
}

public enum AnimationPlaybackMode
{
    PlayOnce,
    PlayForDuration
}

public enum AnimationDirection
{
    Forward,
    Reverse
}

[Serializable]
public class StoryStep
{
    public StoryStepType type;

    [HideInInspector]
    public StoryCharacter character;


    // =============================
    // Movement
    // =============================

    public MovementType movementType;
    public Transform targetPoint;

    [Min(0.01f)]
    public float duration = 1f;

    public float jumpHeight = 2f;


    // =============================
    // Dialogue
    // =============================

    [TextArea(2, 5)]
    public string dialogueText;


    // =============================
    // Animation
    // =============================

    public string animationName;

    public AnimationPlaybackMode animationPlaybackMode =
        AnimationPlaybackMode.PlayOnce;

    public AnimationDirection animationDirection =
        AnimationDirection.Forward;

    [Min(0.01f)]
    public float animationDuration = 1f;

    public bool returnToIdle = true;

    // =============================
    // Wait
    // =============================

    [Min(0f)]
    public float waitDuration = 0.5f;
    public bool keepDialogueVisible = false;
}