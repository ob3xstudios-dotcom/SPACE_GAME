using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class PlayerManaUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Game.Player.PlayerMana mana;

        [Header("UI")]
        [SerializeField] private Image targetImage;
        [SerializeField] private Sprite fullSprite;
        [SerializeField] private Sprite halfSprite;
        [SerializeField] private Sprite emptySprite;

        [Header("Tuning")]
        [SerializeField, Range(1, 50)] private int maxFallback = 3;
        [SerializeField] private bool logBindingOnce = true;

        private Coroutine bindRoutine;
        private bool subscribed;
        private bool loggedBinding;

        private void Awake()
        {
            ResolveMana();

            if (targetImage == null)
                targetImage = GetComponent<Image>();
        }

        private void OnEnable()
        {
            bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void Start()
        {
            ResolveMana();
            Subscribe();
            RefreshFromMana();
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
            while (mana == null)
            {
                ResolveMana();
                if (mana == null)
                    yield return null;
            }

            Subscribe();
            RefreshFromMana();
            bindRoutine = null;
        }

        private void ResolveMana()
        {
            if (mana == null)
                mana = FindFirstObjectByType<Game.Player.PlayerMana>();
        }

        private void Subscribe()
        {
            if (subscribed || mana == null) return;

            mana.OnManaChanged += Refresh;
            subscribed = true;

            if (logBindingOnce && !loggedBinding)
            {
                Debug.Log($"[MANA UI] Bound to {mana.name} ({mana.CurrentMana}/{mana.MaxMana})");
                loggedBinding = true;
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed || mana == null) return;

            mana.OnManaChanged -= Refresh;
            subscribed = false;
        }

        private void RefreshFromMana()
        {
            int current = mana != null ? mana.CurrentMana : 0;
            int max = mana != null ? mana.MaxMana : maxFallback;
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
