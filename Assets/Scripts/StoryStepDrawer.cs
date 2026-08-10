using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(StoryStep))]
public class StoryStepDrawer : PropertyDrawer
{
    private const float Spacing = 4f;
    private const float BottomPadding = 10f;

    private const int DialogueTextLines = 3;


    // =====================================================
    // DRAW
    // =====================================================

    public override void OnGUI(
        Rect position,
        SerializedProperty property,
        GUIContent label)
    {
        EditorGUI.BeginProperty(
            position,
            label,
            property
        );


        float lineHeight =
            EditorGUIUtility.singleLineHeight;


        SerializedProperty typeProperty =
            property.FindPropertyRelative(
                "type"
            );

        SerializedProperty characterProperty =
            property.FindPropertyRelative(
                "character"
            );

        SerializedProperty movementTypeProperty =
            property.FindPropertyRelative(
                "movementType"
            );

        SerializedProperty targetPointProperty =
            property.FindPropertyRelative(
                "targetPoint"
            );

        SerializedProperty durationProperty =
            property.FindPropertyRelative(
                "duration"
            );

        SerializedProperty jumpHeightProperty =
            property.FindPropertyRelative(
                "jumpHeight"
            );

        SerializedProperty dialogueTextProperty =
            property.FindPropertyRelative(
                "dialogueText"
            );

        SerializedProperty animationNameProperty =
            property.FindPropertyRelative(
                "animationName"
            );

        SerializedProperty animationDurationProperty =
            property.FindPropertyRelative(
                "animationDuration"
            );

        SerializedProperty returnToIdleProperty =
            property.FindPropertyRelative(
                "returnToIdle"
            );

        SerializedProperty waitDurationProperty =
            property.FindPropertyRelative(
                "waitDuration"
            );

        SerializedProperty keepDialogueVisibleProperty =
            property.FindPropertyRelative(
                "keepDialogueVisible"
            );

        SerializedProperty animationPlaybackModeProperty =
            property.FindPropertyRelative(
                "animationPlaybackMode"
            );

        SerializedProperty animationDirectionProperty =
            property.FindPropertyRelative(
                "animationDirection"
            );


        Rect rect =
            new Rect(
                position.x,
                position.y,
                position.width,
                lineHeight
            );


        // =====================================================
        // TYPE
        // =====================================================

        EditorGUI.PropertyField(
            rect,
            typeProperty,
            new GUIContent("Type")
        );

        MoveNext(ref rect);

        StoryStepType stepType =
            (StoryStepType)
            typeProperty.enumValueIndex;


        // Wait does not need a character
        if (stepType != StoryStepType.Wait)
        {
            DrawCharacterDropdown(
                rect,
                property,
                characterProperty
            );

            MoveNext(ref rect);
        }


        // =====================================================
        // MOVEMENT
        // =====================================================

        if (stepType ==
            StoryStepType.Movement)
        {
            EditorGUI.PropertyField(
                rect,
                movementTypeProperty,
                new GUIContent(
                    "Movement Type"
                )
            );

            MoveNext(ref rect);


            EditorGUI.PropertyField(
                rect,
                targetPointProperty,
                new GUIContent(
                    "Target Point"
                )
            );

            MoveNext(ref rect);


            EditorGUI.PropertyField(
                rect,
                durationProperty,
                new GUIContent(
                    "Duration"
                )
            );

            MoveNext(ref rect);


            MovementType movementType =
                (MovementType)
                movementTypeProperty.enumValueIndex;


            if (movementType ==
                MovementType.Jump)
            {
                EditorGUI.PropertyField(
                    rect,
                    jumpHeightProperty,
                    new GUIContent(
                        "Jump Height"
                    )
                );
            }
        }


        // =====================================================
        // DIALOGUE
        // =====================================================

        else if (stepType ==
                 StoryStepType.Dialogue)
        {
            EditorGUI.LabelField(
                rect,
                "Dialogue Text"
            );

            MoveNext(ref rect);


            float textAreaHeight =
                lineHeight *
                DialogueTextLines;


            Rect textAreaRect =
                new Rect(
                    rect.x,
                    rect.y,
                    rect.width,
                    textAreaHeight
                );


            dialogueTextProperty.stringValue =
                EditorGUI.TextArea(
                    textAreaRect,
                    dialogueTextProperty.stringValue
                );
        }

        // =====================================================
        // ANIMATION
        // =====================================================

        else if (stepType ==
         StoryStepType.Animation)
        {
            EditorGUI.PropertyField(
                rect,
                animationNameProperty,
                new GUIContent("Animation Name")
            );

            MoveNext(ref rect);


            EditorGUI.PropertyField(
                rect,
                animationPlaybackModeProperty,
                new GUIContent("Playback Mode")
            );

            MoveNext(ref rect);


            EditorGUI.PropertyField(
                rect,
                animationDirectionProperty,
                new GUIContent("Direction")
            );

            MoveNext(ref rect);


            AnimationPlaybackMode playbackMode =
                (AnimationPlaybackMode)
                animationPlaybackModeProperty.enumValueIndex;


            if (playbackMode ==
                AnimationPlaybackMode.PlayForDuration)
            {
                EditorGUI.PropertyField(
                    rect,
                    animationDurationProperty,
                    new GUIContent("Duration")
                );

                MoveNext(ref rect);
            }


            EditorGUI.PropertyField(
                rect,
                returnToIdleProperty,
                new GUIContent("Return To Idle")
            );
        }

        // =====================================================
        // WAIT
        // =====================================================

        else if (stepType == StoryStepType.Wait)
        {
            EditorGUI.PropertyField(
                rect,
                waitDurationProperty,
                new GUIContent("Duration")
            );

            MoveNext(ref rect);

            EditorGUI.PropertyField(
                rect,
                keepDialogueVisibleProperty,
                new GUIContent("Keep Dialogue Visible")
            );
        }


        EditorGUI.EndProperty();
    }


    // =====================================================
    // NEXT LINE
    // =====================================================

    private void MoveNext(
        ref Rect rect)
    {
        rect.y +=
            EditorGUIUtility.singleLineHeight
            + Spacing;
    }


    // =====================================================
    // CHARACTER DROPDOWN
    // =====================================================

    private void DrawCharacterDropdown(
        Rect rect,
        SerializedProperty property,
        SerializedProperty characterProperty)
    {
        SerializedObject serializedObject =
            property.serializedObject;


        SerializedProperty charactersProperty =
            serializedObject.FindProperty(
                "storyCharacters"
            );


        if (charactersProperty == null ||
            charactersProperty.arraySize == 0)
        {
            EditorGUI.LabelField(
                rect,
                "Character",
                "No Story Characters"
            );

            return;
        }


        List<string> names =
            new List<string>();

        List<StoryCharacter> characters =
            new List<StoryCharacter>();


        // Get every StoryCharacter
        // from StoryController list
        for (int i = 0;
             i < charactersProperty.arraySize;
             i++)
        {
            SerializedProperty element =
                charactersProperty
                    .GetArrayElementAtIndex(i);


            StoryCharacter character =
                element.objectReferenceValue
                    as StoryCharacter;


            if (character == null)
                continue;


            characters.Add(character);


            if (string.IsNullOrWhiteSpace(
                character.DisplayName))
            {
                names.Add(
                    character.gameObject.name
                );
            }
            else
            {
                names.Add(
                    character.DisplayName
                );
            }
        }


        if (characters.Count == 0)
        {
            EditorGUI.LabelField(
                rect,
                "Character",
                "No Valid Characters"
            );

            return;
        }


        StoryCharacter currentCharacter =
            characterProperty.objectReferenceValue
                as StoryCharacter;


        int currentIndex =
            characters.IndexOf(
                currentCharacter
            );


        // If nothing selected,
        // automatically use first character
        if (currentIndex < 0)
        {
            currentIndex = 0;

            characterProperty.objectReferenceValue =
                characters[0];
        }


        int newIndex =
            EditorGUI.Popup(
                rect,
                "Character",
                currentIndex,
                names.ToArray()
            );


        // Only update when selection changes
        if (newIndex != currentIndex &&
            newIndex >= 0 &&
            newIndex < characters.Count)
        {
            characterProperty.objectReferenceValue =
                characters[newIndex];
        }
    }


    // =====================================================
    // HEIGHT
    // =====================================================

    public override float GetPropertyHeight(
    SerializedProperty property,
    GUIContent label)
    {
        float lineHeight =
            EditorGUIUtility.singleLineHeight;

        SerializedProperty typeProperty =
            property.FindPropertyRelative("type");

        StoryStepType stepType =
            (StoryStepType)
            typeProperty.enumValueIndex;


        // Type
        float height =
            lineHeight + Spacing;


        // Character
        // Wait does not need one
        if (stepType != StoryStepType.Wait)
        {
            height +=
                lineHeight + Spacing;
        }


        if (stepType == StoryStepType.Movement)
        {
            // Movement Type
            // Target Point
            // Duration
            height +=
                (lineHeight + Spacing) * 3;

            SerializedProperty movementTypeProperty =
                property.FindPropertyRelative(
                    "movementType"
                );

            MovementType movementType =
                (MovementType)
                movementTypeProperty.enumValueIndex;

            if (movementType == MovementType.Jump)
            {
                // Jump Height
                height +=
                    lineHeight + Spacing;
            }
        }

        else if (stepType == StoryStepType.Dialogue)
        {
            // Dialogue label
            height +=
                lineHeight + Spacing;

            // Dialogue Text Area
            height +=
                lineHeight * DialogueTextLines;
        }

        else if (stepType ==
         StoryStepType.Animation)
        {
            // Animation Name
            // Playback Mode
            // Direction
            // Return To Idle

            height +=
                (lineHeight + Spacing) * 4;


            SerializedProperty playbackModeProperty =
                property.FindPropertyRelative(
                    "animationPlaybackMode"
                );


            AnimationPlaybackMode playbackMode =
                (AnimationPlaybackMode)
                playbackModeProperty.enumValueIndex;


            if (playbackMode ==
                AnimationPlaybackMode.PlayForDuration)
            {
                // Duration
                height +=
                    lineHeight + Spacing;
            }
        }

        else if (stepType == StoryStepType.Wait)
        {
            height +=
                (lineHeight + Spacing) * 2;
        }


        height += BottomPadding;

        return height;
    }
}