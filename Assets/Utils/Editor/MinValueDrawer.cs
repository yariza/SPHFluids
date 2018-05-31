using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(MinValueAttribute))]
public class MinValueDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.PropertyField(position, property);
        
        var min = attribute as MinValueAttribute;

        if (property.propertyType == SerializedPropertyType.Float)
        {
            property.floatValue = Mathf.Max(property.floatValue, min.min);
        }
        else if (property.propertyType == SerializedPropertyType.Integer)
        {
            property.intValue = Mathf.Max(property.intValue, (int)min.min);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}
