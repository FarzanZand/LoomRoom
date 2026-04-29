namespace MFPC
{
    using UnityEngine;

    [CreateAssetMenu(fileName = "Surface SFX", menuName = "Surface SFX")]
    public class SurfaceSFX : ScriptableObject
    {
        [Header("Sound effects")]

        [Tooltip("Footstep sounds played while walking on this surface.")]
        public AudioClip[] walkSounds;
        [Tooltip("Sounds played when landing on this surface after a jump or fall.")]
        public AudioClip[] jumpLandSounds;


        [Header("Set-up")]

        [Tooltip("Terrain layers associated with this surface sound preset.")]
        public TerrainLayer[] layers;
    }
}