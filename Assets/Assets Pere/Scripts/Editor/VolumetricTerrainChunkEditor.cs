using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumetricTerrainChunk))]
public class VolumetricTerrainChunkEditor : Editor {
    private const float BrushRadius = 3f;
    private const float BrushStrength = 0.5f;

    // === SELECCIÓN DE MATERIAL ===
    private TerrainMaterial selectedMaterial;

    private void OnSceneGUI() {
        if (Event.current.type == EventType.MouseMove)
            SceneView.RepaintAll();

        VolumetricTerrainChunk targetChunk = (VolumetricTerrainChunk)target;
        Event currentEvent = Event.current;

        Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);

        // === RAYCAST FÍSICO ===
        bool hasHit = Physics.Raycast(ray, out RaycastHit hit);
        if (hasHit) {
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(hit.point, hit.normal, BrushRadius);
            Handles.SphereHandleCap(0, hit.point, Quaternion.identity, BrushRadius * 0.1f, EventType.Repaint);
        }

        // === INPUT ===
        if ((currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag) && currentEvent.button == 0) {
            if (GUIUtility.hotControl == 0)
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);

            if (hasHit) {
                BrushType brushType;
                if (currentEvent.control) brushType = BrushType.Flatten;
                else if (currentEvent.shift) brushType = BrushType.SphereSubtract;
                else brushType = BrushType.SphereAdd;

                byte matID = selectedMaterial != null ? selectedMaterial.materialID : (byte)0;
                targetChunk.ModifyTerrain(hit.point, BrushRadius, BrushStrength, brushType, matID);
                currentEvent.Use();
            }
        }

        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && GUIUtility.hotControl != 0)
            GUIUtility.hotControl = 0;
    }

    public override void OnInspectorGUI() {
        base.OnInspectorGUI();
        VolumetricTerrainChunk script = (VolumetricTerrainChunk)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("// === PINCEL DE MATERIAL ===", EditorStyles.boldLabel);
        selectedMaterial = (TerrainMaterial)EditorGUILayout.ObjectField("Material activo", selectedMaterial, typeof(TerrainMaterial), false);
        if (selectedMaterial != null)
            EditorGUILayout.LabelField($"ID: {selectedMaterial.materialID}", EditorStyles.miniLabel);

        if (!Application.isPlaying && script.islandCount.IsCreated) {
            int count = script.islandCount.Value;
            if (count > 0)
                EditorGUILayout.HelpBox($"ADVERTENCIA: {count} islas flotantes detectadas. Se eliminarán al iniciar el juego.", MessageType.Warning);
        }
    }
}
