using UnityEngine;
using UnityEngine.UI;

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

        private void OnEnable()
        {
            service = CurrencyService.Instance;
            service.OnCurrencyChanged -= HandleCurrencyChanged;
            service.OnCurrencyChanged += HandleCurrencyChanged;
            HandleCurrencyChanged(service.CarriedCoins, service.BankedCoins);
        }

        private void OnDisable()
        {
            if (service != null)
                service.OnCurrencyChanged -= HandleCurrencyChanged;
            service = null;
        }

        private void HandleCurrencyChanged(int carriedCoins, int bankedCoins)
        {
            if (carriedCoinsText != null)
                carriedCoinsText.text = carriedPrefix + carriedCoins;
            if (bankedCoinsText != null)
                bankedCoinsText.text = bankedPrefix + bankedCoins;
        }
    }
}
