using System.Linq;
using UnityEditor;
using UnityEngine;
using FF.Terrain;

namespace FF.Terrain.Editor {

    [CustomEditor(typeof(TerrainMaterialManager))]
    public class TerrainMaterialManagerEditor : UnityEditor.Editor {

        private const string REQUIRED_SHADER = "VoxelTerrain_Triplanar";
        private const string PREVIEW_INDEX_PROP = "_PreviewMaterialIndex";
        private Vector2 scrollPos = Vector2.zero;
        private string searchFilter = "";
        private bool[] foldoutStates;
        private int selectedMaterialIndex = -1;

        private MaterialEditor materialEditor;
        private bool forceRebuildPreview = false;

        private void OnEnable() {
            var manager = (TerrainMaterialManager)target;
            if (foldoutStates == null || foldoutStates.Length != manager.materials.Count)
                foldoutStates = new bool[manager.materials.Count];

            UnityEditor.Editor tempEditor = null;
            CreateCachedEditor(manager.targetMaterial, typeof(MaterialEditor), ref tempEditor);
            materialEditor = (MaterialEditor)tempEditor;
        }

        private void OnDisable() {
            if (materialEditor != null) {
                DestroyImmediate(materialEditor);
                materialEditor = null;
            }
        }

        public override void OnInspectorGUI() {
            var manager = (TerrainMaterialManager)target;

            EditorGUILayout.LabelField("Material Library Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // === TARGET MATERIAL ===
            var targetMaterial = EditorGUILayout.ObjectField(
                new GUIContent("Target Material", "Material que recibirá los Texture2DArray"),
                manager.targetMaterial,
                typeof(Material),
                false
            ) as Material;

            if (targetMaterial != manager.targetMaterial) {
                Undo.RecordObject(manager, "Change Target Material");
                manager.targetMaterial = targetMaterial;
                EditorUtility.SetDirty(manager);
            }

            EditorGUILayout.Space(10f);

            // === Si no hay material, mostrar warning y esconder el resto ===
            if (manager.targetMaterial == null) {
                EditorGUILayout.HelpBox(
                    "⚠ No target material assigned.\n\n" +
                    "Assign a material that uses the terrain shader to continue.",
                    MessageType.Warning
                );
                return;
            }

            // === Validar que el shader sea el correcto ===
            if (!manager.targetMaterial.shader.name.Contains(REQUIRED_SHADER)) {
                EditorGUILayout.HelpBox(
                    $"⚠ Wrong shader detected.\n\n" +
                    $"This material uses '{manager.targetMaterial.shader.name}'.\n" +
                    $"Expected: '{REQUIRED_SHADER}'",
                    MessageType.Error
                );
                return;
            }

            // === TEXTURE DENSITY (readonly) ===
            var density = manager.targetMaterial.GetFloat("_Density");
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField(
                new GUIContent("Texture Density", "Densidad de textura en el shader"),
                density
            );
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(30f);

            // === MATERIALS BOX ===
            DrawMaterialsList(manager);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            EditorGUILayout.Space(10f);

            if (selectedMaterialIndex < 0 || selectedMaterialIndex >= manager.materials.Count)
                EditorGUILayout.HelpBox("Select one material to enable preview.", MessageType.Info);

            // === VALIDAR Y MOSTRAR ADVERTENCIAS ===
            DrawValidationWarnings(manager);

            EditorGUILayout.Space(10f);

            // === BOTON APPLY ===
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("Apply", GUILayout.Height(40f))) {
                if (ValidateMaterials(manager))
                    manager.RefreshAll();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(20f);

            if (forceRebuildPreview) {
                if (materialEditor != null) {
                    DestroyImmediate(materialEditor);
                    materialEditor = null;
                }
                forceRebuildPreview = false;
            }

            if (targetMaterial != null) {
                if (materialEditor == null || materialEditor.target != targetMaterial) {
                    if (materialEditor != null) DestroyImmediate(materialEditor);
                    UnityEditor.Editor tempEditor = null;
                    CreateCachedEditor(manager.targetMaterial, typeof(MaterialEditor), ref tempEditor);
                    materialEditor = (MaterialEditor)tempEditor;
                }
            }
            else if (materialEditor != null) {
                DestroyImmediate(materialEditor);
                materialEditor = null;
            }
        }



        public override bool HasPreviewGUI() {
            var manager = (TerrainMaterialManager)target;
            if (manager == null || manager.targetMaterial == null) return false;
            if (selectedMaterialIndex < 0 || selectedMaterialIndex >= manager.materials.Count) return false;
            return materialEditor != null ? materialEditor.HasPreviewGUI() : false;
        }
        public override void OnPreviewSettings() {
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background) {
            var manager = (TerrainMaterialManager)target;
            ApplyPreviewIndex(manager);
            if (materialEditor != null)
                materialEditor.OnPreviewGUI(r, background);
        }

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background) {
            var manager = (TerrainMaterialManager)target;
            ApplyPreviewIndex(manager);
            if (materialEditor != null)
                materialEditor.OnInteractivePreviewGUI(r, background);
        }

        public override bool RequiresConstantRepaint() {
            if (materialEditor != null) return materialEditor.RequiresConstantRepaint();
            return false;
        }

        void ApplyPreviewIndex(TerrainMaterialManager manager) {
            if (manager == null || manager.targetMaterial == null) return;
            if (!manager.targetMaterial.HasProperty(PREVIEW_INDEX_PROP)) return;

            float indexValue = -1f;
            if (selectedMaterialIndex >= 0 && selectedMaterialIndex < manager.materials.Count)
                indexValue = selectedMaterialIndex;

            manager.targetMaterial.SetFloat(PREVIEW_INDEX_PROP, indexValue);
            Shader.SetGlobalFloat(PREVIEW_INDEX_PROP, indexValue);
        }



        void DrawMaterialsList(TerrainMaterialManager manager) {
            EditorGUILayout.LabelField("Materials", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(10f);

            // === SEARCH BAR INTEGRADA ===
            searchFilter = EditorGUILayout.TextField(
                new GUIContent("Search", "Buscar material por nombre"),
                searchFilter,
                GUILayout.Height(25f)
            );
            EditorGUILayout.Space(5f);
            // === MATERIALS BOX CON SCROLLBAR ===
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300f));

            if (manager.materials.Count == 0) {
                EditorGUILayout.HelpBox("No materials added yet.", MessageType.Info);
            }
            else {
                for (int i = 0; i < manager.materials.Count; i++) {
                    var mat = manager.materials[i];

                    // Filtro de búsqueda
                    if (!string.IsNullOrEmpty(searchFilter) &&
                        !mat.materialName.ToLower().Contains(searchFilter.ToLower()))
                        continue;

                    DrawMaterialElement(manager, i);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5f);

            // === BOTONES AÑADIR/ELIMINAR ===
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+", GUILayout.Width(40f))) {
                Undo.RecordObject(manager, "Add Material");
                manager.materials.Add(new TerrainMaterialEntry { materialID = (uint)manager.materials.Count });
                EditorUtility.SetDirty(manager);
            }
            if (GUILayout.Button("-", GUILayout.Width(40f)) && manager.materials.Count > 0) {
                Undo.RecordObject(manager, "Remove Material");
                manager.materials.RemoveAt(manager.materials.Count - 1);
                EditorUtility.SetDirty(manager);
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawMaterialElement(TerrainMaterialManager manager, int index) {
            var mat = manager.materials[index];

            var isSelected = selectedMaterialIndex == index;
            var prevColor = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.35f, 0.6f, 0.95f);
            var itemRect = EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = prevColor;

            // === HEADER CON FOLDOUT (nombre del material) ===
            EditorGUILayout.BeginHorizontal();
            if (foldoutStates.Length <= index) System.Array.Resize(ref foldoutStates, index + 1);
            foldoutStates[index] = EditorGUILayout.Foldout(foldoutStates[index], $"{mat.materialName}");

            // Botón eliminar
            if (GUILayout.Button("×", GUILayout.Width(25f))) {
                Undo.RecordObject(manager, "Remove Material");
                manager.materials.RemoveAt(index);
                if (selectedMaterialIndex == index) selectedMaterialIndex = -1;
                else if (selectedMaterialIndex > index) selectedMaterialIndex--;
                EditorUtility.SetDirty(manager);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (foldoutStates[index]) {
                EditorGUI.indentLevel++;

                // === MATERIAL ID (readonly) ===
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.IntField("Material ID", (int)mat.materialID);
                EditorGUI.EndDisabledGroup();

                // === MATERIAL NAME ===
                var newName = EditorGUILayout.TextField("Material Name", mat.materialName);
                if (newName != mat.materialName) {
                    mat.materialName = newName;
                    EditorUtility.SetDirty(manager);
                }

                EditorGUILayout.Space(5f);

                // === VISUAL PROPERTIES (colapsable) ===
                bool visualExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                    mat.visualExpanded ?? false,
                    "Visual Properties",
                    EditorStyles.foldoutHeader
                );
                mat.visualExpanded = visualExpanded;

                if (visualExpanded) {
                    EditorGUI.indentLevel++;

                    // Albedo (obligatorio)
                    var albedo = EditorGUILayout.ObjectField("Albedo", mat.albedo, typeof(Texture2D), false) as Texture2D;
                    if (albedo != mat.albedo) {
                        mat.albedo = albedo;
                        EditorUtility.SetDirty(manager);
                    }

                    // Normal (opcional)
                    var normal = EditorGUILayout.ObjectField("Normal (Optional)", mat.normal, typeof(Texture2D), false) as Texture2D;
                    if (normal != mat.normal) {
                        mat.normal = normal;
                        EditorUtility.SetDirty(manager);
                    }

                    EditorGUILayout.Space(5f);

                    var rough = EditorGUILayout.Slider("Roughness", mat.roughness, 0f, 1f);
                    if (rough != mat.roughness) {
                        mat.roughness = rough;
                        EditorUtility.SetDirty(manager);
                    }

                    var metal = EditorGUILayout.Slider("Metallic", mat.metallic, 0f, 1f);
                    if (metal != mat.metallic) {
                        mat.metallic = metal;
                        EditorUtility.SetDirty(manager);
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndFoldoutHeaderGroup();

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            var clickEvent = Event.current;
            if (clickEvent.type == EventType.MouseDown && itemRect.Contains(clickEvent.mousePosition)) {
                selectedMaterialIndex = index;
                ApplyPreviewIndex(manager);
                Repaint();
            }

            EditorGUILayout.Space(5f);
        }

        void DrawValidationWarnings(TerrainMaterialManager manager) {
            // === VALIDAR NOMBRES DUPLICADOS ===
            var duplicateNames = manager.materials
                .GroupBy(m => m.materialName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNames.Count > 0) {
                EditorGUILayout.HelpBox(
                    $"⚠ Duplicate material names detected: {string.Join(", ", duplicateNames)}",
                    MessageType.Warning
                );
            }

            // === VALIDAR TEXTURAS FALTANTES (solo albedo es obligatorio) ===
            var incomplete = manager.materials.Where(m => m.albedo == null).ToList();
            if (incomplete.Count > 0) {
                var list = FormatMaterialList(incomplete);
                EditorGUILayout.HelpBox(
                    $"⚠ {incomplete.Count} material(s) missing albedo texture:\n{list}",
                    MessageType.Warning
                );
            }
        }

        bool ValidateMaterials(TerrainMaterialManager manager) {
            // Comprobar nombres duplicados
            var duplicateNames = manager.materials
                .GroupBy(m => m.materialName)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateNames.Count > 0) {
                EditorUtility.DisplayDialog(
                    "Validation Error",
                    $"Duplicate material names found:\n{string.Join("\n", duplicateNames.Select(g => g.Key))}",
                    "OK"
                );
                return false;
            }

            // Comprobar texturas faltantes (solo albedo es obligatorio)
            var incomplete = manager.materials.Where(m => m.albedo == null).ToList();
            if (incomplete.Count > 0) {
                var list = FormatMaterialList(incomplete);
                EditorUtility.DisplayDialog(
                    "Validation Error",
                    $"{incomplete.Count} material(s) missing albedo texture:\n{list}",
                    "OK"
                );
                return false;
            }

            return true;
        }

        string FormatMaterialList(System.Collections.Generic.List<TerrainMaterialEntry> materials, int maxDisplay = 5) {
            var names = materials.Select(m => $"'{m.materialName}'").ToList();

            if (names.Count <= maxDisplay) {
                return string.Join(", ", names);
            }

            var displayed = string.Join(", ", names.Take(maxDisplay));
            var remaining = names.Count - maxDisplay;
            return $"{displayed}, and {remaining} other{(remaining > 1 ? "s" : "")}...";
        }
    }
}
