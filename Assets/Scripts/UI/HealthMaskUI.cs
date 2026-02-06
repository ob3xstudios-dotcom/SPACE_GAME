using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Game.Player;

namespace Game.UI
{
    public class HealthMasksUI : MonoBehaviour
    {
        [SerializeField] private PlayerResources playerResources;
        [SerializeField] private Image maskPrefab;
        [SerializeField] private Transform container;
        [SerializeField] private Sprite fullSprite;
        [SerializeField] private Sprite emptySprite;

        private Image[] masks = new Image[0];

        private void OnEnable()
        {
            StartCoroutine(BindWhenReady());
        }

        private IEnumerator BindWhenReady()
        {
            if (playerResources == null)
                playerResources = FindObjectOfType<PlayerResources>();

            // ✅ esperar hasta que exista Health (Awake ya corrió)
            while (playerResources != null && playerResources.Health == null)
                yield return null;

            if (playerResources == null || playerResources.Health == null)
            {
                Debug.LogError("[UI] HealthMasksUI: no PlayerResources/Health");
                yield break;
            }

            playerResources.Health.OnChanged += Refresh;
            Refresh(playerResources.Health.Current, playerResources.Health.Max);
        }

        private void OnDisable()
        {
            if (playerResources != null && playerResources.Health != null)
                playerResources.Health.OnChanged -= Refresh;
        }

        private void Refresh(int current, int max)
        {
            EnsureMaskCount(max);
            for (int i = 0; i < masks.Length; i++)
                masks[i].sprite = (i < current) ? fullSprite : emptySprite;
        }

        private void EnsureMaskCount(int max)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
                Destroy(container.GetChild(i).gameObject);

            masks = new Image[max];
            for (int i = 0; i < max; i++)
                masks[i] = Instantiate(maskPrefab, container);
        }
    }
}
