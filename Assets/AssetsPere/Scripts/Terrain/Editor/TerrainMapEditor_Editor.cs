using UnityEditor;
using UnityEngine;

namespace FF.Terrain.Editor {
    public partial class TerrainMapEditor : UnityEditor.Editor {
        private static float brushRadius = 3f;
        private static FF.Terrain.Edit.BrushShape brushShape = FF.Terrain.Edit.BrushShape.Sphere;
        private static FF.Terrain.Edit.CSGOperation csgOperation = FF.Terrain.Edit.CSGOperation.Add;
        private static uint paintMaterialId = 1;
        private static EditMode currentMode = EditMode.Modify;

        public enum EditMode { Modify, Paint }

        private void DrawEditTab() {
            EditorGUILayout.LabelField("Terrain Editor Tools", EditorStyles.boldLabel);
            brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.1f, 20f);
            brushShape = (FF.Terrain.Edit.BrushShape)EditorGUILayout.EnumPopup("Brush Shape", brushShape);
            csgOperation = (FF.Terrain.Edit.CSGOperation)EditorGUILayout.EnumPopup("CSG Operation", csgOperation);
            currentMode = (EditMode)EditorGUILayout.EnumPopup("Edit Mode", currentMode);
            paintMaterialId = (uint)EditorGUILayout.IntField("Paint Material ID", (int)paintMaterialId);
        }

        public void OnSceneGUI() {
            if (!isEditing) return;

            TerrainMap map = (TerrainMap)target;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                Handles.color = Color.cyan;
                if (brushShape == FF.Terrain.Edit.BrushShape.Sphere)
                    Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
                else
                    Handles.DrawWireCube(hit.point, Vector3.one * brushRadius * 2f);

                Event e = Event.current;
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0) {
                    if (currentMode == EditMode.Modify)
                        FF.Terrain.Edit.TerrainEditor.ModifySDF(map, hit.point, brushRadius, csgOperation, brushShape);

                    else if (currentMode == EditMode.Paint)
                        FF.Terrain.Edit.TerrainEditor.PaintMaterial(map, hit.point, brushRadius, brushShape, paintMaterialId);

                    e.Use();
                }
            }
            if (Event.current.type == EventType.MouseMove)
                SceneView.RepaintAll();
        }
    }
}
