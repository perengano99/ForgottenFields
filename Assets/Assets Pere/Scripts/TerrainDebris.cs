using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(MeshCollider))]
public class TerrainDebris : MonoBehaviour {
    private VolumetricTerrainChunk originChunk;
    private float debrisRadius = 1.5f;
    private float debrisStrength = 1f;

    public void Initialize(VolumetricTerrainChunk chunk, float volume) {
        originChunk = chunk;
        debrisRadius = volume;
    }

    // === FÍSICA DE IMPACTO ===
    private void OnCollisionEnter(Collision collision) {
        if (collision.gameObject.CompareTag("Debris")) return;
        if (originChunk == null || collision.contactCount == 0) return;

        originChunk.ModifyTerrain(collision.contacts[0].point, debrisRadius, debrisStrength, BrushType.SphereAdd);
        Destroy(gameObject);
    }
}

// TODO: AL IMPACTAR, EL VOLUMEN AÑADIDO A LA MALLA NO CORRESPONDE AL VOLUMEN y FOMRA DEL DEBRIS, SINO QUE ES UNA ESFERA DE RADIO FIJO. HAY QUE CALCULAR EL VOLUMEN REAL DEL DEBRIS Y AÑADIRLO A LA MALLA CON LA FORMA CORRESPONDIENTE.