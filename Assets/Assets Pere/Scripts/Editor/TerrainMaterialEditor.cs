using System.Reflection;
using UnityEditor;

[CustomEditor(typeof(TerrainMaterial))]
public class TerrainMaterialEditor : Editor {
    public override void OnInspectorGUI() {
        serializedObject.Update();

        TerrainMaterial material = (TerrainMaterial)target;

        using (new EditorGUI.DisabledScope(true)) 
            EditorGUILayout.TextField("materialName", material.name);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("materialID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isSolid"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("canFloat"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isModifiable"));

        serializedObject.ApplyModifiedProperties();

        FieldInfo conflictField = typeof(TerrainMaterial).GetField("idConflict", BindingFlags.Instance | BindingFlags.NonPublic);
        if (conflictField == null) return;

        bool hasConflict = (bool)conflictField.GetValue(material);
        if (!hasConflict) return;

        EditorGUILayout.HelpBox("ERROR: Esta ID ya está siendo usada por otro material. Cámbiala para evitar corrupción de datos.", MessageType.Error);
    }
}
