using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DynamicTerrainManager))]
public class DynamicTerrainManagerEditor : Editor {
    // === ESTADO INTERNO DEL EDITOR ===
    private string[] materialNames = new string[0];
    private int[] materialIDs = new int[0];
    private int materialPopupIndex = 0;

    private void OnEnable() => RefreshMaterialList();

    private void RefreshMaterialList() {
        if (MaterialRegistry.Instance != null)
            MaterialRegistry.Instance.InitializeIfNeeded();

        TerrainMaterial[] assets = Resources.LoadAll<TerrainMaterial>("Terrain/Material");

        materialNames = new string[assets.Length + 1];
        materialIDs = new int[assets.Length + 1];
        materialNames[0] = "(0) Default";
        materialIDs[0] = 0;

        for (int i = 0; i < assets.Length; i++) {
            materialNames[i + 1] = $"({assets[i].materialID}) {assets[i].name}";
            materialIDs[i + 1] = assets[i].materialID;
        }
    }

    public override void OnInspectorGUI() {
        DynamicTerrainManager mgr = (DynamicTerrainManager)target;
        serializedObject.Update();

        SerializedProperty editModeProp = serializedObject.FindProperty("currentEditMode");
        SerializedProperty isEditingProp = serializedObject.FindProperty("isEditing");
        SerializedProperty shapeProp = serializedObject.FindProperty("CurrentBrushShape");
        SerializedProperty radiusProp = serializedObject.FindProperty("BrushRadius");
        SerializedProperty strengthProp = serializedObject.FindProperty("BrushStrength");
        SerializedProperty fixedStepProp = serializedObject.FindProperty("FixedStep");

        // === MODO DE EDICIÓN ===
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Edit Mode", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(editModeProp);

        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = isEditingProp.boolValue ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button(isEditingProp.boolValue ? "✏️  SALIR del Modo Edición" : "✏️  ENTRAR al Modo Edición", GUILayout.Height(32))) {
            isEditingProp.boolValue = !isEditingProp.boolValue;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = prevColor;
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // === GLOBAL SETTINGS ===
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("worldSizeX"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("worldSizeY"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("worldSizeZ"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("chunkGridSize"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("voxelSize"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("terrainMaterial"), new GUIContent("Terrain Material"));
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // === BRUSH SETTINGS (CONDICIONAL) ===
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);

        if ((EditMode)editModeProp.enumValueIndex == EditMode.Sculpt) {
            EditorGUILayout.PropertyField(shapeProp, new GUIContent("Brush Shape"));
            EditorGUILayout.PropertyField(radiusProp, new GUIContent("Radius"));
            EditorGUILayout.PropertyField(strengthProp, new GUIContent("Strength"));
            if (fixedStepProp != null)
                EditorGUILayout.PropertyField(fixedStepProp, new GUIContent("FixedStep"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("IsVerticalBrush"), new GUIContent("Vertical Brush"));
        } else {
            EditorGUILayout.PropertyField(radiusProp, new GUIContent("Radius"));
        }

        EditorGUILayout.EndVertical();

        // === MATERIAL SELECTION (SOLO PAINT) ===
        if ((EditMode)editModeProp.enumValueIndex == EditMode.Paint) {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Material Selection", EditorStyles.boldLabel);

            if (GUILayout.Button("Refresh Materials", EditorStyles.miniButton))
                RefreshMaterialList();

            if (MaterialRegistry.Instance == null)
                EditorGUILayout.HelpBox("MaterialRegistry.Instance no está disponible.", MessageType.Warning);

            if (materialNames.Length > 0) {
                for (int i = 0; i < materialIDs.Length; i++)
                    if (materialIDs[i] == mgr.SelectedMaterialID) { materialPopupIndex = i; break; }

                int newIndex = EditorGUILayout.Popup("Material Activo", materialPopupIndex, materialNames);
                if (newIndex != materialPopupIndex) {
                    materialPopupIndex = newIndex;
                    mgr.SelectedMaterialID = (byte)materialIDs[materialPopupIndex];
                }
            } else EditorGUILayout.HelpBox("No hay TerrainMaterial registrados.", MessageType.Warning);

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(4);

        // === ACTIONS ===
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Regenerate Terrain")) {
            if (EditorUtility.DisplayDialog(
                "Regenerar Terreno",
                "¿Estás seguro? Esta acción destruirá y recreará todos los chunks.",
                "Regenerar", "Cancelar")) {
                mgr.SendMessage("RebuildChunks", SendMessageOptions.DontRequireReceiver);
            }
        }

        if (GUILayout.Button("Apply Changes to Grid"))
            mgr.SendMessage("RebuildChunks", SendMessageOptions.DontRequireReceiver);

        EditorGUILayout.EndVertical();

        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI() {
        DynamicTerrainManager mgr = (DynamicTerrainManager)target;
        if (!mgr.isEditing) return;

        Event e = Event.current;
        EventModifiers modifiers = e.modifiers;

        // === ATAJOS DE TECLADO ===
        if (e.type == EventType.KeyDown) {
            if (e.keyCode == KeyCode.Alpha1) { mgr.CurrentBrushShape = BrushShape.Sphere; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha2) { mgr.CurrentBrushShape = BrushShape.Box; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha3) { mgr.CurrentBrushShape = BrushShape.VerticalPillar; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha5) { mgr.CurrentBrushShape = BrushShape.NoiseFeature; e.Use(); }
            Repaint();
        }

        // === RUEDA DEL RATÓN ===
        if (e.type == EventType.ScrollWheel) {
            float delta = -e.delta.y * 0.5f;
            if ((modifiers & EventModifiers.Shift) != 0) {
                mgr.BrushRadius = Mathf.Clamp(mgr.BrushRadius + delta, 0.5f, 50f);
                e.Use();
            } else if ((modifiers & EventModifiers.Control) != 0) {
                mgr.BrushStrength = Mathf.Clamp(mgr.BrushStrength + delta * 0.1f, 0.01f, 5f);
                e.Use();
            }
            Repaint();
        }

        // === RAYCAST Y HANDLE VISUAL ===
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        bool hasHit = Physics.Raycast(ray, out RaycastHit hit);

        if (hasHit) {
            // === COLOR DINÁMICO EN SCULPT ===
            if (mgr.currentEditMode == EditMode.Sculpt) {
                if ((modifiers & EventModifiers.Control) != 0) Handles.color = Color.blue;
                else if ((modifiers & EventModifiers.Shift) != 0) Handles.color = Color.yellow;
                else if ((modifiers & EventModifiers.Alt) != 0) Handles.color = Color.red;
                else Handles.color = Color.green;
            } else Handles.color = new Color(0f, 1f, 0.8f, 0.8f);

            Handles.DrawWireDisc(hit.point, hit.normal, mgr.BrushRadius);
            Handles.DrawWireDisc(hit.point, hit.normal, mgr.BrushRadius * 0.5f);
            Handles.SphereHandleCap(0, hit.point, Quaternion.identity, mgr.BrushRadius * 0.08f, EventType.Repaint);

            Handles.Label(hit.point + Vector3.up * (mgr.BrushRadius + 0.5f),
                $"{mgr.currentEditMode} | {mgr.CurrentBrushShape}\nR: {mgr.BrushRadius:F1}  S: {mgr.BrushStrength:F2}");
        }

        // === LEYENDA EN PANTALLA ===
        if (mgr.currentEditMode == EditMode.Sculpt) {
            Handles.BeginGUI();
            GUI.Box(new Rect(10, 10, 430, 24), "LMB: Add | Alt+LMB: Sub | Shift+LMB: Flatten | Ctrl+LMB: Smooth");
            Handles.EndGUI();
        }

        int controlID = GUIUtility.GetControlID(FocusType.Passive);

        // === CONSUMIR CLICK IZQUIERDO (EVITAR PÉRDIDA DE SELECCIÓN) ===
        if (e.type == EventType.MouseDown && e.button == 0 && hasHit) {
            GUIUtility.hotControl = controlID;
            e.Use();
        }

        // === PINTAR / ESCULPIR ===
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && hasHit) {
            List<TerrainChunk> affectedChunks = CollectAffectedChunks(mgr, hit.point);
            if (affectedChunks.Count > 0) {
                if (mgr.currentEditMode == EditMode.Paint)
                    mgr.ApplyPaintBrush(hit.point, affectedChunks);
                else {
                    SculptMode mode;
                    if ((modifiers & EventModifiers.Control) != 0) mode = SculptMode.Smooth;
                    else if ((modifiers & EventModifiers.Shift) != 0) mode = SculptMode.Flatten;
                    else if ((modifiers & EventModifiers.Alt) != 0) mode = SculptMode.Subtract;
                    else mode = SculptMode.Add;

                    mgr.ApplySculptBrush(hit.point, mode, 0, affectedChunks);
                }
            }

            if (e.type == EventType.MouseDrag)
                e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == controlID)
            GUIUtility.hotControl = 0;

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            SceneView.RepaintAll();
    }

    private static List<TerrainChunk> CollectAffectedChunks(DynamicTerrainManager mgr, Vector3 hitPoint) {
        List<TerrainChunk> affectedChunks = new List<TerrainChunk>();
        TerrainChunk[,,] chunks = mgr.Chunks;
        if (chunks == null) return affectedChunks;

        Bounds brushBounds = new Bounds(hitPoint, Vector3.one * (mgr.BrushRadius * 2f));

        for (int x = 0; x < chunks.GetLength(0); x++) {
            for (int y = 0; y < chunks.GetLength(1); y++) {
                for (int z = 0; z < chunks.GetLength(2); z++) {
                    TerrainChunk chunk = chunks[x, y, z];
                    if (chunk == null) continue;

                    Collider col = chunk.GetComponent<Collider>();
                    Bounds chunkBounds = col != null ? col.bounds : new Bounds(chunk.transform.position, Vector3.zero);
                    if (!chunkBounds.Intersects(brushBounds)) continue;

                    affectedChunks.Add(chunk);
                }
            }
        }

        return affectedChunks;
    }
}
