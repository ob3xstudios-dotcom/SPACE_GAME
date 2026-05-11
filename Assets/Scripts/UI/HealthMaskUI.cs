using System.Collections;
using Game.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class HealthMasksUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerResources playerResources;

        [Header("UI")]
        [SerializeField] private Image targetImage;
        [SerializeField] private Sprite fullSprite;
        [SerializeField] private Sprite halfSprite;
        [SerializeField] private Sprite emptySprite;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Coroutine bindRoutine;

        private void Awake()
        {
            if (targetImage == null)
                targetImage = GetComponent<Image>();
        }

        private void OnEnable()
        {
            bindRoutine = StartCoroutine(BindWhenReady());
        }

        private IEnumerator BindWhenReady()
        {
            if (playerResources == null)
                playerResources = FindFirstObjectByType<PlayerResources>();

            while (playerResources != null && playerResources.Health == null)
                yield return null;

            if (playerResources == null || playerResources.Health == null)
            {
                Debug.LogError("[UI] HealthMasksUI: no PlayerResources/Health");
                bindRoutine = null;
                yield break;
            }

            playerResources.Health.OnChanged += Refresh;
            Refresh(playerResources.Health.Current, playerResources.Health.Max);
            bindRoutine = null;
        }

        private void OnDisable()
        {
            if (bindRoutine != null)
            {
                StopCoroutine(bindRoutine);
                bindRoutine = null;
            }

            if (playerResources != null && playerResources.Health != null)
                playerResources.Health.OnChanged -= Refresh;
        }

        private void Refresh(int current, int max)
        {
            if (debugLogs)
                Debug.Log($"[UI] HealthMasksUI Refresh {current}/{max}");

            if (targetImage == null) return;

            max = Mathf.Max(0, max);
            current = Mathf.Clamp(current, 0, max);

            if (current >= max && max > 0)
                targetImage.sprite = fullSprite;
            else if (current > 0)
                targetImage.sprite = halfSprite;
            else
                targetImage.sprite = emptySprite;
        }
    }
}
