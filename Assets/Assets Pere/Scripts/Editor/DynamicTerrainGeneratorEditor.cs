using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DynamicTerrainGenerator))]
public class DynamicTerrainGeneratorEditor : Editor {
    // === EDIT MODE ===
    private enum TerrainTool {
        RaiseLower = 0,
        Smooth = 1
    }

    private static bool isEditModeEnabled;
    private static TerrainTool activeTool = TerrainTool.RaiseLower;

    // === SCENE GUI ===
    private void OnSceneGUI() {
        if (!isEditModeEnabled) return;

        // === REPAINT EVENT ===
        if (Event.current.type == EventType.MouseMove)
            SceneView.RepaintAll();

        DynamicTerrainGenerator terrainGenerator = (DynamicTerrainGenerator)target;
        Transform terrainTransform = terrainGenerator.transform;

        // === RAYCAST & INTERSECTION ===
        Plane terrainPlane = new Plane(Vector3.up, terrainTransform.position);
        Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

        if (!terrainPlane.Raycast(mouseRay, out float hitDistance))
            return;

        Vector3 hitPoint = mouseRay.GetPoint(hitDistance);

        // === VISUALIZACION ===
        float brushRadius = terrainGenerator.GetBrushRadius();

        Handles.color = activeTool == TerrainTool.Smooth ? Color.cyan : Color.yellow;
        Handles.DrawWireDisc(hitPoint, Vector3.up, brushRadius);

        // === INPUT HANDLING ===
        Event currentEvent = Event.current;

        if ((currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag) && currentEvent.button == 0) {
            if (GUIUtility.hotControl == 0)
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);

            Undo.RecordObject(terrainGenerator, activeTool == TerrainTool.Smooth ? "Smooth Terrain" : "Modify Terrain");

            if (activeTool == TerrainTool.Smooth) terrainGenerator.SmoothTerrain(hitPoint);
            else {
                bool isElevating = !currentEvent.shift;
                terrainGenerator.ModifyTerrain(hitPoint, isElevating);
            }

            EditorUtility.SetDirty(terrainGenerator);

            currentEvent.Use();
            SceneView.RepaintAll();
        }

        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && GUIUtility.hotControl != 0) {
            GUIUtility.hotControl = 0;
            currentEvent.Use();
        }
    }

    // === INSPECTOR GUI ===
    public override void OnInspectorGUI() {
        serializedObject.Update();

        // === PROPERTIES ===
        EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("length"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cellSize"));

        GUILayout.Space(4);
        DynamicTerrainGenerator terrainGenerator = (DynamicTerrainGenerator)target;

        if (GUILayout.Button("Apply")) {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(terrainGenerator, "Apply Terrain Settings");
            terrainGenerator.ApplySettings();
            EditorUtility.SetDirty(terrainGenerator);
            SceneView.RepaintAll();
            return;
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Edit", EditorStyles.boldLabel);

        if (GUILayout.Button(isEditModeEnabled ? "Exit Edit Mode" : "Enter Edit Mode")) {
            isEditModeEnabled = !isEditModeEnabled;
            GUIUtility.hotControl = 0;
            SceneView.RepaintAll();
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty("brushRadius"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("brushStrength"));

        activeTool = (TerrainTool)EditorGUILayout.EnumPopup("Tool", activeTool);

        if (GUILayout.Button("Regenerate Mesh")) {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(terrainGenerator, "Regenerate Mesh");
            terrainGenerator.RegenerateMesh();
            EditorUtility.SetDirty(terrainGenerator);
            SceneView.RepaintAll();
            return;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
