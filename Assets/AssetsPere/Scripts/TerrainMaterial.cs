using UnityEngine;

public struct VoxelMaterialData {
    public byte id;
    public byte physicsFlags; // Bits: 1=Solid, 2=CanFloat, 4=Modifiable
    // TODO: textureIndex y otros metadatos se añadirá después.
}

[CreateAssetMenu(fileName = "NewTerrainMaterial", menuName = "Terrain/Material")]
public class TerrainMaterial : ScriptableObject {
    public byte materialID;
    public Texture2D diffuseTexture;
    public bool isSolid = true;
    public bool canFloat = false;
    public bool isModifiable = true;
    [Tooltip("Si es false, este material caerá si no está conectado al suelo.")] public bool isStructural = true;
    [Tooltip("Permite definir si esta textura se proyectará predominantemente en caras verticales (ej. roca) o planas (ej. pasto).")]
    [Range(0f, 1f)] public float verticalProjectionBias = 0.5f;
    [HideInInspector] public byte packedPhysicsFlags;

    private bool idConflict = false;

    private void OnValidate() {

        packedPhysicsFlags = 0;
        if (isSolid) packedPhysicsFlags |= 1;
        if (canFloat) packedPhysicsFlags |= 2;
        if (isModifiable) packedPhysicsFlags |= 4;

        // === VALIDACIÓN DE CONFLICTOS ===
        TerrainMaterial[] allMaterials = Resources.LoadAll<TerrainMaterial>("Terrain/Material");

        for (int i = 0; i < allMaterials.Length; i++) {
            TerrainMaterial other = allMaterials[i];
            if (other == null || other == this) continue;
            if (other.materialID != materialID) continue;

            idConflict = true;
            Debug.LogError($"Conflicto de materialID ({materialID}) entre '{name}' y '{other.name}'.", this);
            return;
        }

        idConflict = false;
    }
}
