using System.Collections.Generic;
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
        [Range(0f, 1f)] public float metallic   = 0f;

        [System.NonSerialized]
        public bool? visualExpanded;
    }

    // === SCRIPTABLE OBJECT: LIBRERIA DE MATERIALES DE TERRENO ===
    [CreateAssetMenu(fileName = "TerrainMaterialLibrary", menuName = "FF/Terrain/Material Library")]
    public class TerrainMaterialManager : ScriptableObject {

        public List<TerrainMaterialEntry> materials = new List<TerrainMaterialEntry>();

        [Header("Texture Array Settings")]
        [Tooltip("Resolución de cada slice (todas las texturas se redimensionarán a esto)")]
        public int textureResolution = 512;

        [Header("Sync Target")]
        [Tooltip("Material que recibirá los Texture2DArray")]
        public Material targetMaterial;

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

            if (albedo != null) targetMaterial.SetTexture("_TextureMap", albedo);
            if (normal != null) targetMaterial.SetTexture("_NormalsMap", normal);
            if (matData != null) targetMaterial.SetTexture("_MaterialData", matData);

            Debug.Log($"[TerrainMaterialManager] Refreshed {materials.Count} materials.");
        }

        // === CONSTRUCCION DEL TEXTURE2DARRAY DE ALBEDO ===
        Texture2DArray BuildAlbedoArray() {
            if (materials == null || materials.Count == 0) return null;

            int res   = textureResolution;
            int count = materials.Count;
            var array = new Texture2DArray(res, res, count, TextureFormat.RGBA32, true, false);
            array.wrapMode   = TextureWrapMode.Repeat;
            array.filterMode = FilterMode.Bilinear;
            array.name       = "TerrainAlbedo";

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

            int res   = textureResolution;
            int count = materials.Count;
            var array = new Texture2DArray(res, res, count, TextureFormat.RGBA32, true, true);
            array.wrapMode   = TextureWrapMode.Repeat;
            array.filterMode = FilterMode.Bilinear;
            array.name       = "TerrainNormals";

            for (int i = 0; i < count; i++) {
                var src = materials[i].normal;
                if (src == null) {
                    Debug.LogWarning($"[TerrainMaterialManager] Material {i} ({materials[i].materialName}) sin normal map.");
                    continue;
                }
                var scaled = ScaleTexture(src, res, res);
                array.SetPixels(scaled.GetPixels(), i);
                Object.DestroyImmediate(scaled);
            }

            array.Apply();
            return array;
        }

        // === CONSTRUCCION DEL TEXTURE2D MATERIAL DATA (1 × 256) ===
        // Cada píxel X = ID material, Y = 0
        // R = roughness, G = metallic, B = reserved, A = reserved
        Texture2D BuildMaterialData() {
            if (materials == null) return null;

            int width = 256;
            var matData = new Texture2D(width, 1, TextureFormat.RGBAHalf, false, true);
            matData.name = "MaterialData";
            matData.filterMode = FilterMode.Point;
            matData.wrapMode   = TextureWrapMode.Clamp;

            Color[] pixels = new Color[width];

            for (int i = 0; i < width; i++)
                pixels[i] = new Color(0.5f, 0f, 0f, 0f);

            for (int i = 0; i < materials.Count && i < width; i++) {
                var mat = materials[i];
                pixels[i] = new Color(mat.roughness, mat.metallic, 0f, 0f);
            }

            matData.SetPixels(pixels);
            matData.Apply();
            return matData;
        }

        static Texture2D ScaleTexture(Texture2D src, int w, int h) {
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}

