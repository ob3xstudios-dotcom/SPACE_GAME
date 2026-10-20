using Game.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class ProgressionStatusHUD : MonoBehaviour
    {
        [SerializeField] private DynamicHudVisibilityController visibilityController;
        [SerializeField] private Text tokenCountText;
        [SerializeField] private Text notificationText;
        [SerializeField] private string tokenPrefix = "Tokens: ";
        [SerializeField] private string abilityUnlockedPrefix = "Habilidad desbloqueada: ";

        private ProgressionService service;

        private void OnEnable()
        {
            if (visibilityController == null)
                visibilityController = GetComponentInParent<DynamicHudVisibilityController>();

            service = ProgressionService.Instance;
            service.OnProgressionLoaded -= HandleProgressionLoaded;
            service.OnTokenCollected -= HandleTokenCollected;
            service.OnAbilityUnlocked -= HandleAbilityUnlocked;
            service.OnProgressionLoaded += HandleProgressionLoaded;
            service.OnTokenCollected += HandleTokenCollected;
            service.OnAbilityUnlocked += HandleAbilityUnlocked;

            RefreshTokenCount();
            if (notificationText != null)
                notificationText.text = string.Empty;
        }

        private void OnDisable()
        {
            if (service != null)
            {
                service.OnProgressionLoaded -= HandleProgressionLoaded;
                service.OnTokenCollected -= HandleTokenCollected;
                service.OnAbilityUnlocked -= HandleAbilityUnlocked;
            }

            service = null;
        }

        private void HandleProgressionLoaded()
        {
            // Loading synchronizes content but is not player activity.
            RefreshTokenCount();
        }

        private void HandleTokenCollected(TokenDefinition definition)
        {
            RefreshTokenCount();
            visibilityController?.ShowEconomy();
        }

        private void HandleAbilityUnlocked(AbilityDefinition definition)
        {
            if (notificationText != null)
            {
                string displayName = definition != null &&
                    !string.IsNullOrWhiteSpace(definition.DisplayName)
                        ? definition.DisplayName
                        : "Habilidad";
                notificationText.text = abilityUnlockedPrefix + displayName;
            }

            visibilityController?.ShowNotifications();
        }

        private void RefreshTokenCount()
        {
            if (tokenCountText == null)
                return;

            int count = service != null ? service.CollectedTokenCount : 0;
            tokenCountText.text = tokenPrefix + count;
        }
    }
}
