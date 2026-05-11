using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class PlayerStaminaUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Game.Player.PlayerStamina stamina;

        [Header("UI")]
        [SerializeField] private Image targetImage;
        [SerializeField] private Sprite fullSprite;
        [SerializeField] private Sprite halfSprite;
        [SerializeField] private Sprite emptySprite;

        [Header("Tuning")]
        [SerializeField, Range(1, 50)] private int maxFallback = 3;
        [SerializeField] private bool logBindingOnce = false;

        private Coroutine bindRoutine;
        private bool subscribed;
        private bool loggedBinding;

        private void Awake()
        {
            ResolveStamina();

            if (targetImage == null)
                targetImage = GetComponent<Image>();
        }

        private void OnEnable()
        {
            bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void Start()
        {
            ResolveStamina();
            Subscribe();
            RefreshFromStamina();
        }

        private void OnDisable()
        {
            if (bindRoutine != null)
            {
                StopCoroutine(bindRoutine);
                bindRoutine = null;
            }

            Unsubscribe();
        }

        private IEnumerator BindWhenReady()
        {
            while (stamina == null)
            {
                ResolveStamina();
                if (stamina == null)
                    yield return null;
            }

            Subscribe();
            RefreshFromStamina();
            bindRoutine = null;
        }

        private void ResolveStamina()
        {
            if (stamina == null)
                stamina = FindFirstObjectByType<Game.Player.PlayerStamina>();
        }

        private void Subscribe()
        {
            if (subscribed || stamina == null) return;

            stamina.OnStaminaChanged += Refresh;
            subscribed = true;

            if (logBindingOnce && !loggedBinding)
            {
                Debug.Log($"[STAMINA UI] Bound to {stamina.name} ({stamina.CurrentStamina}/{stamina.MaxStamina})");
                loggedBinding = true;
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed || stamina == null) return;

            stamina.OnStaminaChanged -= Refresh;
            subscribed = false;
        }

        private void RefreshFromStamina()
        {
            int current = stamina != null ? stamina.CurrentStamina : 0;
            int max = stamina != null ? stamina.MaxStamina : maxFallback;
            Refresh(current, max);
        }

        public void Refresh(int current, int max)
        {
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
