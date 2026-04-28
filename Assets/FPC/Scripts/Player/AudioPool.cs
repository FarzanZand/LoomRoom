namespace MFPC
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    public class AudioSourcePool : MonoBehaviour
    {
        [Tooltip("Prefab used to create pooled audio sources. Must contain an AudioSource component.")]
        [SerializeField] private GameObject audioPrefab;
        [Tooltip("Number of AudioSources created at startup. Increase this value if many sound effects may play at the same time.")]
        [Min(1)]
        [SerializeField] private int initialSize = 12;

        private readonly Queue<AudioSource> pool = new Queue<AudioSource>();

        void Awake()
        {
            for (int i = 0; i < initialSize; i++)
            {
                CreateSource();
            }
        }

        private void CreateSource()
        {
            GameObject go = Instantiate(audioPrefab, transform);
            go.SetActive(false);
            pool.Enqueue(go.GetComponent<AudioSource>());
        }

        public AudioSource Play(
            AudioClip clip,
            Vector3 position,
            float volume = 1f,
            float pitch = 1f)
        {
            if (pool.Count == 0)
                return null;

            AudioSource src = pool.Dequeue();
            src.transform.position = position;
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;

            src.gameObject.SetActive(true);
            src.Play();

            StartCoroutine(ReturnAfterPlay(src));
            return src;
        }

        private IEnumerator ReturnAfterPlay(AudioSource src)
        {
            yield return new WaitWhile(() => src.isPlaying);
            src.gameObject.SetActive(false);
            pool.Enqueue(src);
        }
    }
}