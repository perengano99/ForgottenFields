using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FF.Terrain {

    // === ENTRADA DE MATERIAL DE TERRENO ===
    [System.Serializable]
    public class TerrainMaterialEntry {
        [Tooltip("ID único del material (0-255)")]
        public uint materialID;
        public string materialName;
        public Texture2D albedo;
        public Texture2D normal;
        [Range(0f, 1f)] public float roughness = 0.5f;
        [Range(0f, 1f)] public float metallic = 0f;

        [System.NonSerialized]
        public bool? visualExpanded;
    }

    // === SCRIPTABLE OBJECT: LIBRERIA DE MATERIALES DE TERRENO ===
    [CreateAssetMenu(fileName = "TerrainMaterialLibrary", menuName = "FF/Terrain/Material Library")]
    public class TerrainMaterialManager : ScriptableObject {

        public List<TerrainMaterialEntry> materials = new List<TerrainMaterialEntry>();

        private static int textureResolution = 512;

        [Header("Sync Target")]
        [Tooltip("Material que recibirá los Texture2DArray")]
        public Material targetMaterial;

        [SerializeField, HideInInspector] private Texture2DArray savedAlbedo;
        [SerializeField, HideInInspector] private Texture2DArray savedNormal;
        [SerializeField, HideInInspector] private Texture2D savedMatData;

        private void OnEnable() => SyncMaterial();

        // === LOOKUP: materialID → indice de lista ===
        public int IndexOf(uint materialID) {
            for (int i = 0; i < materials.Count; i++)
                if (materials[i].materialID == materialID) return i;
            return -1;
        }

        // === REFRESH GLOBAL: Regenera y sincroniza todas las texturas ===
        public void RefreshAll() {
            if (targetMaterial == null) {
                Debug.LogWarning("[TerrainMaterialManager] No target material assigned.");
                return;
            }
            if (materials == null || materials.Count == 0) {
                Debug.LogWarning("[TerrainMaterialManager] No materials in library.");
                return;
            }

            var albedo = BuildAlbedoArray();
            var normal = BuildNormalArray();
            var matData = BuildMaterialData();

            savedAlbedo = albedo;
            savedNormal = normal;
            savedMatData = matData;

#if UNITY_EDITOR
            if (!Application.isPlaying) {
                string assetPath = AssetDatabase.GetAssetPath(this);
                if (!string.IsNullOrEmpty(assetPath)) {
                    // Limpieza de sub-assets huérfanos anteriores
                    Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (Object obj in allAssets) {
                        if (obj != this && (obj is Texture2D || obj is Texture2DArray))
                            DestroyImmediate(obj, true);
                    }

                    // Inyección de nueva memoria al AssetDatabase
                    if (albedo != null) AssetDatabase.AddObjectToAsset(albedo, this);
                    if (normal != null) AssetDatabase.AddObjectToAsset(normal, this);
                    if (matData != null) AssetDatabase.AddObjectToAsset(matData, this);

                    EditorUtility.SetDirty(this);
                    if (targetMaterial != null)
                        EditorUtility.SetDirty(targetMaterial);
                    AssetDatabase.SaveAssets();
                }
            }
#endif
            SyncMaterial();
        }

        public void SyncMaterial() {
            if (targetMaterial == null) return;
            if (savedAlbedo != null) targetMaterial.SetTexture("_TextureMap", savedAlbedo);
            if (savedNormal != null) targetMaterial.SetTexture("_NormalsMap", savedNormal);
            if (savedMatData != null) targetMaterial.SetTexture("_MaterialData", savedMatData);
        }

        // === CONSTRUCCION DEL TEXTURE2DARRAY DE ALBEDO ===
        Texture2DArray BuildAlbedoArray() {
            if (materials == null || materials.Count == 0) return null;

            int res = textureResolution;
            int count = materials.Count;
            var array = new Texture2DArray(res, res, count, TextureFormat.RGBA32, false, false);
            array.wrapMode = TextureWrapMode.Repeat;
            array.filterMode = FilterMode.Point;
            array.name = "TerrainAlbedo";

            for (int i = 0; i < count; i++) {
                var src = materials[i].albedo;
                if (src == null) {
                    Debug.LogWarning($"[TerrainMaterialManager] Material {i} ({materials[i].materialName}) sin albedo.");
                    continue;
                }
                var scaled = ScaleTexture(src, res, res);
                array.SetPixels(scaled.GetPixels(), i);
                Object.DestroyImmediate(scaled);
            }

            array.Apply();
            return array;
        }

        // === CONSTRUCCION DEL TEXTURE2DARRAY DE NORMALES ===
        Texture2DArray BuildNormalArray() {
            if (materials == null || materials.Count == 0) return null;

            int res = textureResolution;
            int count = materials.Count;
            var array = new Texture2DArray(res, res, count, TextureFormat.RGBA32, false, true);
            array.wrapMode = TextureWrapMode.Repeat;
            array.filterMode = FilterMode.Point;
            array.name = "TerrainNormals";
            Color[] flatNormalPixels = null;

            for (int i = 0; i < count; i++) {
                var src = materials[i].normal;
                if (src == null) {
                    if (flatNormalPixels == null) {
                        flatNormalPixels = new Color[res * res];
                        for (int j = 0; j < flatNormalPixels.Length; j++)
                            flatNormalPixels[j] = new Color(0.5f, 0.5f, 1f); // Normal plano
                    }
                    array.SetPixels(flatNormalPixels, i);
                    continue;
                }
                var scaled = ScaleTexture(src, res, res);
                array.SetPixels(scaled.GetPixels(), i);
                DestroyImmediate(scaled);
            }

            array.Apply();
            return array;
        }

        // === CONSTRUCCION DEL TEXTURE2D MATERIAL DATA (1 × 256) ===
        // Cada píxel X = ID material, Y = 0
        // R = roughness, G = metallic, B = has Normal, A = reserved
        Texture2D BuildMaterialData() {
            if (materials == null) return null;

            int width = 256;
            var matData = new Texture2D(width, 1, TextureFormat.RGBAHalf, false, true);
            matData.name = "MaterialData";
            matData.filterMode = FilterMode.Point;
            matData.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[width];

            for (int i = 0; i < width; i++)
                pixels[i] = new Color(0.5f, 0f, 0f, 0f);

            for (int i = 0; i < materials.Count && i < width; i++) {
                var mat = materials[i];
                pixels[i] = new Color(mat.roughness, mat.metallic, mat.normal != null ? 1f : 0f, 0f);
            }

            matData.SetPixels(pixels);
            matData.Apply();
            return matData;
        }

        static Texture2D ScaleTexture(Texture2D src, int w, int h) {
            var rt = RenderTexture.GetTemporary(w, h, 0);
            src.filterMode = FilterMode.Point;
            rt.filterMode = FilterMode.Point;

            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.filterMode = FilterMode.Point;

            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}

