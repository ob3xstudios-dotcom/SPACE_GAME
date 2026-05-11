using System.Collections;
using UnityEngine;

namespace Game.Systems
{
    public class HitStopManager : MonoBehaviour
    {
        private static HitStopManager instance;

        private float restoreTimeScale = 1f;
        private float stopUntilRealtime;
        private Coroutine hitStopCo;

        public static void Request(float seconds)
        {
            if (seconds <= 0f) return;

            var manager = GetOrCreate();
            manager.RequestInternal(seconds);
        }

        private static HitStopManager GetOrCreate()
        {
            if (instance != null) return instance;

            instance = FindFirstObjectByType<HitStopManager>();
            if (instance != null) return instance;

            var go = new GameObject(nameof(HitStopManager));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HitStopManager>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (instance != this) return;

            if (hitStopCo != null && Time.timeScale == 0f)
                Time.timeScale = restoreTimeScale <= 0f ? 1f : restoreTimeScale;

            instance = null;
        }

        private void RequestInternal(float seconds)
        {
            stopUntilRealtime = Mathf.Max(stopUntilRealtime, Time.unscaledTime + seconds);

            if (hitStopCo != null) return;

            restoreTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
            Time.timeScale = 0f;
            hitStopCo = StartCoroutine(HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            while (Time.unscaledTime < stopUntilRealtime)
                yield return null;

            Time.timeScale = restoreTimeScale <= 0f ? 1f : restoreTimeScale;
            hitStopCo = null;
        }
    }
}
