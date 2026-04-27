using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumetricTerrainChunk))]
public class VolumetricTerrainChunkEditor : Editor {
    private const float BrushRadius = 3f;
    private const float BrushStrength = 0.5f;

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

                targetChunk.ModifyTerrain(hit.point, BrushRadius, BrushStrength, brushType);
                currentEvent.Use();
            }
        }

        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && GUIUtility.hotControl != 0)
            GUIUtility.hotControl = 0;
    }
}
