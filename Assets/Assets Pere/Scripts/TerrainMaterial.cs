using UnityEngine;

public struct VoxelMaterialData {
    public byte id;
    public byte physicsFlags; // Bits: 1=Solid, 2=CanFloat, 4=Modifiable
    // TODO: textureIndex y otros metadatos se añadirán después.
}

[CreateAssetMenu(fileName = "NewTerrainMaterial", menuName = "Terrain/Material")]
public class TerrainMaterial : ScriptableObject {
    public byte materialID;
    public string materialName;
    public bool isSolid = true;
    public bool canFloat = false;
    public bool isModifiable = true;
    [HideInInspector] public byte packedPhysicsFlags;

    private void OnValidate() {
        packedPhysicsFlags = 0;
        if (isSolid) packedPhysicsFlags |= 1;
        if (canFloat) packedPhysicsFlags |= 2;
        if (isModifiable) packedPhysicsFlags |= 4;
    }
}
