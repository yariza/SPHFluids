using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SPHSpawner))]
public class SPHSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var spawner = target as SPHSpawner;
        EditorGUILayout.LabelField("Num Particles: " + spawner.particleCount);
    }
}
