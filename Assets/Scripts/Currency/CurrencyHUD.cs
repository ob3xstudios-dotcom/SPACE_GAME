using UnityEngine;
using UnityEngine.UI;
using Game.UI;

namespace Game.Currency
{
    /// <summary>Optional event-driven diagnostic HUD. Add it to an existing Canvas.</summary>
    [DisallowMultipleComponent]
    public sealed class CurrencyHUD : MonoBehaviour
    {
        [SerializeField] private Text carriedCoinsText;
        [SerializeField] private Text bankedCoinsText;
        [SerializeField] private string carriedPrefix = "Cartera: ";
        [SerializeField] private string bankedPrefix = "Banco: ";

        private CurrencyService service;
        private DynamicHudVisibilityController visibilityController;
        private CanvasGroup carriedCoinsVisibility;
        private CanvasGroup bankedCoinsVisibility;
        private bool hasSnapshot;
        private int lastCarriedCoins;
        private int lastBankedCoins;

        private void OnEnable()
        {
            visibilityController = GetComponentInParent<DynamicHudVisibilityController>();
            RegisterVisibilityTargets();

            service = CurrencyService.Instance;
            service.OnCurrencyChanged -= HandleCurrencyChanged;
            service.OnEconomyLoaded -= HandleEconomyLoaded;
            service.OnCurrencyChanged += HandleCurrencyChanged;
            service.OnEconomyLoaded += HandleEconomyLoaded;

            hasSnapshot = false;
            ApplyCurrency(service.CarriedCoins, service.BankedCoins, false);
        }

        private void OnDisable()
        {
            if (service != null)
            {
                service.OnCurrencyChanged -= HandleCurrencyChanged;
                service.OnEconomyLoaded -= HandleEconomyLoaded;
            }

            UnregisterVisibilityTargets();
            service = null;
            visibilityController = null;
            hasSnapshot = false;
        }

        private void HandleCurrencyChanged(int carriedCoins, int bankedCoins)
        {
            ApplyCurrency(carriedCoins, bankedCoins, true);
        }

        private void HandleEconomyLoaded()
        {
            if (service != null)
                ApplyCurrency(service.CarriedCoins, service.BankedCoins, true);
        }

        private void ApplyCurrency(int carriedCoins, int bankedCoins, bool notifyChange)
        {
            if (carriedCoinsText != null)
                carriedCoinsText.text = carriedPrefix + carriedCoins;
            if (bankedCoinsText != null)
                bankedCoinsText.text = bankedPrefix + bankedCoins;

            bool changed = hasSnapshot &&
                (carriedCoins != lastCarriedCoins || bankedCoins != lastBankedCoins);
            lastCarriedCoins = carriedCoins;
            lastBankedCoins = bankedCoins;
            hasSnapshot = true;

            if (notifyChange && changed)
                visibilityController?.ShowEconomy();
        }

        private void RegisterVisibilityTargets()
        {
            carriedCoinsVisibility = EnsureCanvasGroup(carriedCoinsText);
            bankedCoinsVisibility = EnsureCanvasGroup(bankedCoinsText);

            if (visibilityController == null)
                return;

            visibilityController.RegisterEconomyTarget(carriedCoinsVisibility);
            visibilityController.RegisterEconomyTarget(bankedCoinsVisibility);
        }

        private void UnregisterVisibilityTargets()
        {
            if (visibilityController == null)
                return;

            visibilityController.UnregisterEconomyTarget(carriedCoinsVisibility);
            visibilityController.UnregisterEconomyTarget(bankedCoinsVisibility);
        }

        private static CanvasGroup EnsureCanvasGroup(Text text)
        {
            if (text == null)
                return null;

            CanvasGroup group = text.GetComponent<CanvasGroup>();
            if (group == null)
                group = text.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }
    }
}
