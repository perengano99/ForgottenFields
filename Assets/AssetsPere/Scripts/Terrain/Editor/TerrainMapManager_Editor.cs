using UnityEditor;
using UnityEngine;

namespace FF.Terrain.Editor {
    [CustomEditor(typeof(TerrainMap))]
    public partial class TerrainMapEditor : UnityEditor.Editor {
        private int selectedTab = 0;
        private string[] tabNames = { "General", "Terrain Editing" };
        private bool isEditing = false;

        private void OnDisable() {
            isEditing = false;
        }

        public override void OnInspectorGUI() {
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
            EditorGUILayout.Space();

            switch (selectedTab) {
                case 0:
                    base.OnInspectorGUI();
                    isEditing = false;
                    break;
                case 1:
                    DrawEditTab();
                    isEditing = true;
                    break;
            }
        }
    }
}
