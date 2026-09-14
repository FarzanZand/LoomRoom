namespace MFPC
{
    using UnityEngine;

    public class TerrainChecker : MonoBehaviour
    {
        private float[] GetTextureMix(Vector3 playerPos, Terrain t)
        {
            Vector3 tPos = t.transform.position;
            TerrainData terrainData = t.terrainData;

            // player position relative to the terrain
            int mapX = Mathf.FloorToInt((playerPos.x - tPos.x) / terrainData.size.x * terrainData.alphamapWidth);
            int mapZ = Mathf.FloorToInt((playerPos.z - tPos.z) / terrainData.size.z * terrainData.alphamapHeight);

            mapX = Mathf.Clamp(mapX, 0, terrainData.alphamapWidth - 1);
            mapZ = Mathf.Clamp(mapZ, 0, terrainData.alphamapHeight - 1);

            float[,,] splatMapData = terrainData.GetAlphamaps(mapX, mapZ, 1, 1);

            float[] cellMix = new float[splatMapData.GetUpperBound(2) + 1];
            for (int i = 0; i < cellMix.Length; i++)
            {
                cellMix[i] = splatMapData[0, 0, i];
            }
            return cellMix;
        }

        public string GetDominantLayerAtPosition(Vector3 playerPos, Terrain t)
        {
            float[] cellMix = GetTextureMix(playerPos, t);
            float strongest = 0;
            int maxIndex = 0;
            for (int i = 0; i < cellMix.Length; i++)
            {
                if (cellMix[i] > strongest)
                {
                    maxIndex = i;
                    strongest = cellMix[i];
                }
            }
            return t.terrainData.terrainLayers[maxIndex].name;
        }
    }
}