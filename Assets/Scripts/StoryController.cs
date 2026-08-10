using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class StoryController : MonoBehaviour
{
    [Header("Story Sequence")]
    [SerializeField] private List<StoryStep> storySteps;

    [Header("Characters")]
    [SerializeField] private List<StoryCharacter> storyCharacters;

    [Header("Dialogue UI")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TMP_Text speakerNameText;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private GameObject continueIcon;

    [Header("Dialogue Settings")]
    [SerializeField] private float typingSpeed = 0.03f;

    [Header("Speaker Visuals")]
    [SerializeField] private Color activeSpeakerColor = Color.white;
    [SerializeField]
    private Color inactiveSpeakerColor =
        new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Story Camera")]
    [SerializeField] private Camera storyCamera;

    private Vector3 movementCharacterStartPosition;

    private Vector3 movementCameraStartPosition;

    private Vector3 movementCameraEndPosition;

    private int currentStepIndex = -1;

    private StoryStep currentStep;

    private Coroutine currentAction;

    private StoryState currentState = StoryState.None;

    private string currentFullText;

    private float previousAnimatorSpeed = 1f;

    private enum StoryState
    {
        None,
        Movement,
        Animation,
        Wait,
        Typing,
        DialogueComplete,
        Finished
    }


    // =====================================================
    // UNITY
    // =====================================================

    private void Start()
    {
        dialoguePanel.SetActive(false);

        StartNextStep();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetKeyDown(KeyCode.Space))
        {
            HandleAdvanceInput();
        }
    }


    // =====================================================
    // STORY SEQUENCE
    // =====================================================

    private void StartNextStep()
    {
        currentStepIndex++;

        if (currentStepIndex >= storySteps.Count)
        {
            EndStory();
            return;
        }

        currentStep = storySteps[currentStepIndex];

        ExecuteCurrentStep();
    }

    private void ExecuteCurrentStep()
    {
        switch (currentStep.type)
        {
            case StoryStepType.Movement:
                StartMovement(currentStep);
                break;

            case StoryStepType.Dialogue:
                StartDialogue(currentStep);
                break;
            case StoryStepType.Animation:
                StartAnimation(currentStep);
                break;
            case StoryStepType.Wait:
                StartWait(currentStep);
                break;
        }
    }

    private void StartWait(StoryStep step)
    {
        currentState = StoryState.Wait;

        continueIcon.SetActive(false);

        if (!step.keepDialogueVisible)
        {
            dialoguePanel.SetActive(false);

            SetAllCharactersActive();
        }

        currentAction =
            StartCoroutine(
                WaitRoutine(step.waitDuration)
            );
    }

    private IEnumerator WaitRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);

        CompleteWait();
    }

    private void CompleteWait()
    {
        currentAction = null;

        StartNextStep();
    }

    private void StartAnimation(StoryStep step)
    {
        StoryCharacter character =
            step.character;

        if (character == null)
        {
            Debug.LogError(
                $"Story Step {currentStepIndex}: " +
                "Animation is missing Character."
            );

            StartNextStep();
            return;
        }

        if (string.IsNullOrWhiteSpace(
            step.animationName))
        {
            Debug.LogError(
                $"Story Step {currentStepIndex}: " +
                "Animation Name is empty."
            );

            StartNextStep();
            return;
        }

        dialoguePanel.SetActive(false);

        SetAllCharactersActive();

        currentState =
            StoryState.Animation;

        currentAction =
            StartCoroutine(
                PlayStoryAnimation(step, character)
            );
    }

    private IEnumerator PlayStoryAnimation(
    StoryStep step,
    StoryCharacter character)
    {
        // =========================================
        // FORWARD
        // =========================================

        if (step.animationDirection ==
            AnimationDirection.Forward)
        {
            yield return StartCoroutine(
                PlayAnimationForward(
                    step,
                    character
                )
            );
        }


        // =========================================
        // REVERSE
        // =========================================

        else if (step.animationDirection ==
                 AnimationDirection.Reverse)
        {
            yield return StartCoroutine(
                PlayAnimationReverse(
                    step,
                    character
                )
            );
        }


        CompleteAnimation(
            step,
            character
        );
    }

    private IEnumerator PlayAnimationForward(
    StoryStep step,
    StoryCharacter character)
    {
        character.PlayAnimation(
            step.animationName
        );

        // Let Animator enter the state.
        yield return null;


        // =========================================
        // PLAY ONCE
        // =========================================

        if (step.animationPlaybackMode ==
            AnimationPlaybackMode.PlayOnce)
        {
            Animator animator =
                character.Animator;

            while (true)
            {
                AnimatorStateInfo stateInfo =
                    animator.GetCurrentAnimatorStateInfo(0);

                if (stateInfo.normalizedTime >= 1f)
                {
                    break;
                }

                yield return null;
            }
        }


        // =========================================
        // PLAY FOR DURATION
        // =========================================

        else if (step.animationPlaybackMode ==
                 AnimationPlaybackMode.PlayForDuration)
        {
            yield return new WaitForSeconds(
                step.animationDuration
            );
        }
    }

    private IEnumerator PlayAnimationReverse(
    StoryStep step,
    StoryCharacter character)
    {
        Animator animator =
            character.Animator;

        if (animator == null)
        {
            yield break;
        }


        // Save current Animator speed
        previousAnimatorSpeed =
            animator.speed;


        // Start animation from its final frame
        animator.Play(
            step.animationName,
            0,
            1f
        );

        // Evaluate immediately so we can get
        // correct state information
        animator.Update(0f);


        AnimatorStateInfo stateInfo =
            animator.GetCurrentAnimatorStateInfo(0);

        float animationLength =
            stateInfo.length;


        if (animationLength <= 0f)
        {
            animationLength = 0.01f;
        }


        // Freeze normal Animator playback.
        // We will control normalizedTime ourselves.
        animator.speed = 0f;


        float elapsedTime = 0f;


        // =========================================
        // PLAY ONCE REVERSE
        // =========================================

        if (step.animationPlaybackMode ==
            AnimationPlaybackMode.PlayOnce)
        {
            while (elapsedTime < animationLength)
            {
                elapsedTime += Time.deltaTime;

                float progress =
                    Mathf.Clamp01(
                        elapsedTime /
                        animationLength
                    );


                // 1 → 0
                float reversedTime =
                    1f - progress;


                animator.Play(
                    step.animationName,
                    0,
                    reversedTime
                );

                animator.Update(0f);

                yield return null;
            }


            // Guarantee exact first frame
            animator.Play(
                step.animationName,
                0,
                0f
            );

            animator.Update(0f);
        }


        // =========================================
        // PLAY FOR DURATION REVERSE
        // =========================================

        else if (step.animationPlaybackMode ==
                 AnimationPlaybackMode.PlayForDuration)
        {
            elapsedTime = 0f;


            while (elapsedTime <
                   step.animationDuration)
            {
                elapsedTime += Time.deltaTime;


                float loopProgress =
                    Mathf.Repeat(
                        elapsedTime /
                        animationLength,
                        1f
                    );


                // 1 → 0 repeatedly
                float reversedTime =
                    1f - loopProgress;


                animator.Play(
                    step.animationName,
                    0,
                    reversedTime
                );

                animator.Update(0f);

                yield return null;
            }
        }
    }

    private void CompleteAnimation(
    StoryStep step,
    StoryCharacter character)
    {
        Animator animator =
            character.Animator;


        if (animator != null)
        {
            animator.speed =
                previousAnimatorSpeed;
        }


        if (step.returnToIdle)
        {
            character.PlayAnimation(
                character.IdleAnimation
            );
        }


        currentAction = null;

        StartNextStep();
    }


    // =====================================================
    // MOVEMENT
    // =====================================================

    private void StartMovement(StoryStep step)
    {
        StoryCharacter character =
            step.character;

        if (character == null ||
            step.targetPoint == null)
        {
            Debug.LogError(
                $"Story Step {currentStepIndex}: " +
                "Movement is missing Character or Target Point."
            );

            StartNextStep();
            return;
        }

        dialoguePanel.SetActive(false);

        SetAllCharactersActive();

        currentState =
            StoryState.Movement;


        // Save character's starting position
        movementCharacterStartPosition =
            character.transform.position;


        // Prepare camera movement
        PrepareCameraMovement(
            step,
            character
        );


        switch (step.movementType)
        {
            case MovementType.Walk:

                currentAction =
                    StartCoroutine(
                        WalkCharacter(
                            step,
                            character
                        )
                    );

                break;


            case MovementType.Jump:

                currentAction =
                    StartCoroutine(
                        JumpCharacter(
                            step,
                            character
                        )
                    );

                break;
        }
    }

    private void PrepareCameraMovement(
    StoryStep step,
    StoryCharacter character)
    {
        if (storyCamera == null)
            return;


        movementCameraStartPosition =
            storyCamera.transform.position;


        movementCameraEndPosition =
            movementCameraStartPosition;


        if (!step.moveCameraWithCharacter)
            return;


        Vector3 characterMovement =
            step.targetPoint.position -
            character.transform.position;


        if (step.moveCameraX)
        {
            movementCameraEndPosition.x +=
                characterMovement.x;
        }


        if (step.moveCameraY)
        {
            movementCameraEndPosition.y +=
                characterMovement.y;
        }


        // Camera Z must remain unchanged
        movementCameraEndPosition.z =
            movementCameraStartPosition.z;
    }

    private void UpdateCameraMovement(
    StoryStep step,
    float progress)
    {
        if (storyCamera == null ||
            !step.moveCameraWithCharacter)
        {
            return;
        }


        storyCamera.transform.position =
            Vector3.Lerp(
                movementCameraStartPosition,
                movementCameraEndPosition,
                progress
            );
    }


    // =====================================================
    // WALK
    // =====================================================

    private IEnumerator WalkCharacter(
    StoryStep step,
    StoryCharacter character)
    {
        character.PlayAnimation(
            character.WalkAnimation
        );

        Vector3 startPosition =
            character.transform.position;

        Vector3 endPosition =
            step.targetPoint.position;

        float elapsedTime = 0f;

        while (elapsedTime < step.duration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(
                elapsedTime / step.duration
            );

            character.transform.position =
                Vector3.Lerp(
                    startPosition,
                    endPosition,
                    progress
                );

            UpdateCameraMovement(
                step,
                progress
            );

            yield return null;
        }

        CompleteMovement(step, character);
    }


    // =====================================================
    // JUMP
    // =====================================================

    private IEnumerator JumpCharacter(
    StoryStep step,
    StoryCharacter character)
    {
        Vector3 startPosition =
            character.transform.position;

        Vector3 endPosition =
            step.targetPoint.position;

        float elapsedTime = 0f;

        bool switchedToJumpDown = false;

        character.PlayAnimation(
            character.JumpUpAnimation
        );

        while (elapsedTime < step.duration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(
                elapsedTime / step.duration
            );

            Vector3 position =
                Vector3.Lerp(
                    startPosition,
                    endPosition,
                    progress
                );

            float curveHeight =
                Mathf.Sin(progress * Mathf.PI)
                * step.jumpHeight;

            position.y += curveHeight;

            character.transform.position =
                position;

            UpdateCameraMovement(
                step,
                progress
            );

            if (progress >= 0.5f &&
                !switchedToJumpDown)
            {
                switchedToJumpDown = true;

                character.PlayAnimation(
                    character.JumpDownAnimation
                );
            }

            yield return null;
        }

        CompleteMovement(step, character);
    }


    // =====================================================
    // COMPLETE MOVEMENT
    // =====================================================

    private void CompleteMovement(
    StoryStep step,
    StoryCharacter character)
    {
        // Character final position
        character.transform.position =
            step.targetPoint.position;


        // Camera final position
        if (storyCamera != null &&
            step.moveCameraWithCharacter)
        {
            storyCamera.transform.position =
                movementCameraEndPosition;
        }


        character.PlayAnimation(
            character.IdleAnimation
        );


        currentAction = null;


        StartNextStep();
    }


    // =====================================================
    // DIALOGUE
    // =====================================================

    private void StartDialogue(StoryStep step)
    {
        StoryCharacter character =
            step.character;

        if (character == null)
        {
            Debug.LogError(
                $"Story Step {currentStepIndex}: " +
                "Dialogue is missing Character."
            );

            StartNextStep();
            return;
        }

        dialoguePanel.SetActive(true);

        SetActiveSpeaker(character);

        // Automatically use Character Display Name
        speakerNameText.text =
            character.DisplayName;

        currentFullText =
            step.dialogueText;

        continueIcon.SetActive(false);

        currentAction =
            StartCoroutine(
                TypeDialogue(currentFullText)
            );
    }


    // =====================================================
    // TYPEWRITER
    // =====================================================

    private IEnumerator TypeDialogue(string text)
    {
        currentState = StoryState.Typing;

        dialogueText.text = text;

        dialogueText.maxVisibleCharacters = 0;

        dialogueText.ForceMeshUpdate();

        int totalCharacters =
            dialogueText.textInfo.characterCount;

        for (int i = 0;
             i <= totalCharacters;
             i++)
        {
            dialogueText.maxVisibleCharacters = i;

            yield return new WaitForSeconds(
                typingSpeed
            );
        }

        CompleteDialogueTyping();
    }


    private void CompleteDialogueTyping()
    {
        dialogueText.maxVisibleCharacters =
            int.MaxValue;

        currentState =
            StoryState.DialogueComplete;

        currentAction = null;

        continueIcon.SetActive(true);
    }


    // =====================================================
    // INPUT
    // =====================================================

    private void HandleAdvanceInput()
    {
        switch (currentState)
        {
            case StoryState.Movement:
                SkipMovement();
                break;

            case StoryState.Animation:
                SkipAnimation();
                break;

            case StoryState.Wait:
                SkipWait();
                break;

            case StoryState.Typing:
                SkipDialogueTyping();
                break;

            case StoryState.DialogueComplete:
                StartNextStep();
                break;
        }
    }

    private void SkipWait()
    {
        if (currentAction != null)
        {
            StopCoroutine(currentAction);
            currentAction = null;
        }

        CompleteWait();
    }

    private void SkipAnimation()
    {
        if (currentAction != null)
        {
            StopCoroutine(currentAction);
            currentAction = null;
        }

        StoryCharacter character =
            currentStep.character;

        if (character != null)
        {
            CompleteAnimation(
                currentStep,
                character
            );
        }
    }


    // =====================================================
    // SKIP MOVEMENT
    // =====================================================

    private void SkipMovement()
    {
        if (currentAction != null)
        {
            StopCoroutine(currentAction);
            currentAction = null;
        }

        StoryCharacter character =
            currentStep.character;

        if (character != null)
        {
            CompleteMovement(
                currentStep,
                character
            );
        }
    }


    // =====================================================
    // SKIP TYPEWRITER
    // =====================================================

    private void SkipDialogueTyping()
    {
        if (currentAction != null)
        {
            StopCoroutine(currentAction);

            currentAction = null;
        }

        CompleteDialogueTyping();
    }


    // =====================================================
    // SPEAKER VISUAL
    // =====================================================

    private void SetActiveSpeaker(
        StoryCharacter activeCharacter)
    {
        foreach (StoryCharacter character
                 in storyCharacters)
        {
            if (character == null)
                continue;

            if (character == activeCharacter)
            {
                character.SetVisualColor(
                    activeSpeakerColor
                );
            }
            else
            {
                character.SetVisualColor(
                    inactiveSpeakerColor
                );
            }
        }
    }


    private void SetAllCharactersActive()
    {
        foreach (StoryCharacter character
                 in storyCharacters)
        {
            if (character != null)
            {
                character.SetVisualColor(
                    activeSpeakerColor
                );
            }
        }
    }


    // =====================================================
    // END STORY
    // =====================================================

    private void EndStory()
    {
        dialoguePanel.SetActive(false);

        SetAllCharactersActive();

        currentState = StoryState.Finished;

        Debug.Log("Story finished.");
    }
}