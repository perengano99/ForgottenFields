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

        // === MODO EDICIÓN ===
        EditorGUILayout.BeginVertical("box");
        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = mgr.isEditing ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button(mgr.isEditing ? "✏️  SALIR del Modo Edición" : "✏️  ENTRAR al Modo Edición", GUILayout.Height(32))) {
            mgr.isEditing = !mgr.isEditing;
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

        // === BRUSH SETTINGS ===
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);
        mgr.CurrentBrushShape = (BrushShape)EditorGUILayout.EnumPopup("Shape", mgr.CurrentBrushShape);
        mgr.CurrentBrushType = (BrushType)EditorGUILayout.EnumPopup("Type", mgr.CurrentBrushType);
        mgr.BrushRadius = EditorGUILayout.Slider("Radius", mgr.BrushRadius, 0.5f, 50f);
        mgr.BrushStrength = EditorGUILayout.Slider("Strength", mgr.BrushStrength, 0.01f, 5f);
        mgr.IsVerticalBrush = EditorGUILayout.Toggle("Vertical Brush", mgr.IsVerticalBrush);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        // === MATERIAL SELECTION ===
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Material Selection", EditorStyles.boldLabel);

        if (GUILayout.Button("Refresh Materials", EditorStyles.miniButton))
            RefreshMaterialList();

        if (materialNames.Length > 0) {
            // === SINCRONIZAR POPUP CON SelectedMaterialID ===
            for (int i = 0; i < materialIDs.Length; i++)
                if (materialIDs[i] == mgr.SelectedMaterialID) { materialPopupIndex = i; break; }

            int newIndex = EditorGUILayout.Popup("Material Activo", materialPopupIndex, materialNames);
            if (newIndex != materialPopupIndex) {
                materialPopupIndex = newIndex;
                mgr.SelectedMaterialID = (byte)materialIDs[materialPopupIndex];
            }
        } else EditorGUILayout.HelpBox("No hay TerrainMaterial en Resources/Terrain/Material.", MessageType.Warning);

        EditorGUILayout.EndVertical();

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

        // === ATAJOS DE TECLADO ===
        if (e.type == EventType.KeyDown) {
            if (e.keyCode == KeyCode.Alpha1) { mgr.CurrentBrushShape = BrushShape.Sphere; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha2) { mgr.CurrentBrushShape = BrushShape.Cube; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha3) { mgr.CurrentBrushShape = BrushShape.Cylinder; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha4) { mgr.CurrentBrushShape = BrushShape.Cone; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha5) { mgr.CurrentBrushShape = BrushShape.Noise; e.Use(); }
            Repaint();
        }

        // === RUEDA DEL RATÓN ===
        if (e.type == EventType.ScrollWheel) {
            float delta = -e.delta.y * 0.5f;
            if (e.shift) {
                mgr.BrushRadius = Mathf.Clamp(mgr.BrushRadius + delta, 0.5f, 50f);
                e.Use();
            } else if (e.control) {
                mgr.BrushStrength = Mathf.Clamp(mgr.BrushStrength + delta * 0.1f, 0.01f, 5f);
                e.Use();
            }
            Repaint();
        }

        // === RAYCAST Y HANDLE VISUAL ===
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        bool hasHit = Physics.Raycast(ray, out RaycastHit hit);

        if (hasHit) {
            Handles.color = new Color(0f, 1f, 0.8f, 0.8f);
            Handles.DrawWireDisc(hit.point, hit.normal, mgr.BrushRadius);
            Handles.DrawWireDisc(hit.point, hit.normal, mgr.BrushRadius * 0.5f);
            Handles.SphereHandleCap(0, hit.point, Quaternion.identity, mgr.BrushRadius * 0.08f, EventType.Repaint);

            // === LABEL DE ESTADO ===
            Handles.Label(hit.point + Vector3.up * (mgr.BrushRadius + 0.5f),
                $"{mgr.CurrentBrushShape} | {mgr.CurrentBrushType}\nR: {mgr.BrushRadius:F1}  S: {mgr.BrushStrength:F2}");
        }

        // === PINTAR AL HACER CLICK ===
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && hasHit) {
            if (GUIUtility.hotControl == 0)
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);

            mgr.ApplyBrush(hit.point);
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl != 0)
            GUIUtility.hotControl = 0;

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            SceneView.RepaintAll();
    }
}
