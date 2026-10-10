using UnityEngine;

// Footsteps on terrain: the name of the terrain layer painted strongest under a point.
public class TerrainChecker : MonoBehaviour
{
    public string GetDominantLayerAtPosition(Vector3 playerPos, Terrain t)
    {
        Vector3 tPos = t.transform.position;
        TerrainData terrainData = t.terrainData;

        // player position relative to the terrain
        int mapX = Mathf.FloorToInt((playerPos.x - tPos.x) / terrainData.size.x * terrainData.alphamapWidth);
        int mapZ = Mathf.FloorToInt((playerPos.z - tPos.z) / terrainData.size.z * terrainData.alphamapHeight);

        mapX = Mathf.Clamp(mapX, 0, terrainData.alphamapWidth - 1);
        mapZ = Mathf.Clamp(mapZ, 0, terrainData.alphamapHeight - 1);

        // One cell; read straight from the splat data instead of copying it into a second array.
        float[,,] splatMapData = terrainData.GetAlphamaps(mapX, mapZ, 1, 1);
        float strongest = 0;
        int maxIndex = 0;
        for (int i = 0; i <= splatMapData.GetUpperBound(2); i++)
        {
            if (splatMapData[0, 0, i] > strongest)
            {
                maxIndex = i;
                strongest = splatMapData[0, 0, i];
            }
        }
        return terrainData.terrainLayers[maxIndex].name;
    }
}
