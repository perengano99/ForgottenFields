using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MaterialRegistry))]
public class MaterialRegistryEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Materiales Registrados", EditorStyles.boldLabel);

        TerrainMaterial[] materials = Resources.LoadAll<TerrainMaterial>("Terrain/Material");
        Array.Sort(materials, (a, b) => a.materialID.CompareTo(b.materialID));

        for (int i = 0; i < materials.Length; i++) {
            TerrainMaterial mat = materials[i];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(mat.materialID.ToString(), GUILayout.Width(30));

            if (GUILayout.Button(mat.name, EditorStyles.linkLabel, GUILayout.Width(100))) {
                Selection.activeObject = mat;
                EditorGUIUtility.PingObject(mat);
            }

            EditorGUILayout.EndHorizontal();
        }

        GUILayout.Space(10);

        MaterialRegistry registry = (MaterialRegistry)target;
        if (GUILayout.Button("Forzar Re-inicialización"))
            registry.InitializeIfNeeded();
    }
}
