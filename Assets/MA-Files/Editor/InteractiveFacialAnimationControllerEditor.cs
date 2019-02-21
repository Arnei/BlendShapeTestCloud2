﻿using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Adapted from https://www.youtube.com/watch?v=9bHzTDIJX_Q

/*
 * COPIED FROM FACECONTROLLEREDITOR
 * 
 * Provide a graphic interface to
 * - Update references to PlayablesPrototypeV2s
 * - Update emotion List in PlayablesPrototypeV2s
 * - Change emotions
 */

[CustomEditor(typeof(InteractiveFacialAnimationController))]
public class InteractiveFacialAnimationControllerEditor : Editor
{
    InteractiveFacialAnimationController m_Target;

    private bool[] emotionPlays;

    // Override OnInsepctorGUI() to draw your own editor
    public override void OnInspectorGUI()
    {
        m_Target = (InteractiveFacialAnimationController)target;

        emotionPlays = new bool[m_Target.emotionNames.Count];

        GUILayout.Label("Editor Controls", EditorStyles.boldLabel);
        DrawDefaultInspector(); // Draws all the stuff Unity would normally draw
        DrawSearchButton();
        DrawUpdateButton();
        DrawEmotionList();
        GUILayout.Label("Runtime Controls", EditorStyles.boldLabel);
        DrawTransitionButtons();
    }

    void DrawSearchButton()
    {
        if (GUILayout.Button("Search All Head Controllers"))
        {
            Undo.RecordObject(m_Target, "Searched Head Controllers");
            m_Target.getAllHeadControllers();
            EditorUtility.SetDirty(m_Target);
        }
    }

    void DrawUpdateButton()
    {
        if (GUILayout.Button("Update All Head Controllers"))
        {
            Undo.RecordObject(m_Target, "Updated Head Controllers");
            m_Target.updateAllHeadControllers();
            EditorUtility.SetDirty(m_Target);
        }
    }

    void DrawEmotionList()
    {
        GUILayout.Label("Emotions", EditorStyles.boldLabel);

        for (int i = 0; i < m_Target.emotionNames.Count; i++)
        {
            DrawElement(i);
        }

        DrawAddEmotionButton();
    }

    void DrawElement(int index)
    {
        if (index < 0 || index >= m_Target.emotionNames.Count) return;

        //SerializedProperty listIterator = serializedObject.FindProperty("Emotions");

        GUILayout.BeginHorizontal();
        {
            EditorGUI.BeginChangeCheck();
            string newName = GUILayout.TextField(m_Target.emotionNames[index], GUILayout.MinWidth(100), GUILayout.MaxWidth(300));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m_Target, "Modify Emotion"); //Create Undo/Redo Step

                m_Target.emotionNames[index] = newName;

                EditorUtility.SetDirty(m_Target); // If not serizalized, Unity needs to be told that a component has changed so it is saved correctly
            }

            if (GUILayout.Button("Remove", GUILayout.MinWidth(30), GUILayout.MaxWidth(60)))
            {
                Undo.RecordObject(m_Target, "Delete Emotion");
                m_Target.emotionNames.RemoveAt(index);
                EditorUtility.SetDirty(m_Target);
            }
        }
        GUILayout.EndHorizontal();
    }

    void DrawAddEmotionButton()
    {
        if (GUILayout.Button("Add new Emotion", GUILayout.Height(30)))
        {
            Undo.RecordObject(m_Target, "Add new Emotion");
            m_Target.emotionNames.Add("NEW EMOTION");
            EditorUtility.SetDirty(m_Target);
        }
    }

    void DrawTransitionButtons()
    {
        // Draw Transition buttons
        GUILayout.Label("Go to next Emotion", EditorStyles.boldLabel);
        for (int i = 0; i < m_Target.emotionNames.Count; i++)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.ToggleLeft(m_Target.emotionNames[i], emotionPlays[i]);

            if (EditorGUI.EndChangeCheck())
            {
                m_Target.transitionToEmotionAllHeadControllers(m_Target.emotionNames[i]);
            }
        }
    }

}
