using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
    {
        var prev_state= GUI.enabled;
        GUI.enabled = false;
        EditorGUI.PropertyField(pos, prop, label);
        GUI.enabled = prev_state;
    }
}