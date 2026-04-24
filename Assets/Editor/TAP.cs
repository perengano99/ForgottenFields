// === TEXTURE ARRAY PACKER ===
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class TextureArrayPacker : EditorWindow
{
    [SerializeField]
    private Texture2D[] _textures = new Texture2D[0];

    [MenuItem("Tools/Terrain/Texture Array Packer")]
    public static void ShowWindow()
    {
        GetWindow<TextureArrayPacker>("Tex Array Packer");
    }

    private void OnGUI()
    {
        SerializedObject so = new SerializedObject(this);
        SerializedProperty texProp = so.FindProperty("_textures");
        EditorGUILayout.PropertyField(texProp, true);
        so.ApplyModifiedProperties();

        if (GUILayout.Button("Pack Array") && _textures.Length > 0)
        {
            Texture2D t = _textures[0];
            Texture2DArray array = new Texture2DArray(t.width, t.height, _textures.Length, t.format, t.mipmapCount > 1);
            array.filterMode = FilterMode.Point; // Mantiene el Pixel Art

            for (int i = 0; i < _textures.Length; i++)
            {
                if (_textures[i] != null)
                {
                    Graphics.CopyTexture(_textures[i], 0, 0, array, i, 0);
                }
            }

            AssetDatabase.CreateAsset(array, "Assets/TerrainTexArray.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("Texture2DArray Creado en Assets/");
        }
    }
}
#endif