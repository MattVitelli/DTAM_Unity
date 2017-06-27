using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Debug3DMapper))]
public class Debug3DMapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (GUILayout.Button("Build DepthMap"))
        {
            Debug3DMapper t = (Debug3DMapper)target;
            t.Solve();
        }
    }
}