using System;
using Game.Player;
using UnityEngine;

namespace Game.Progression
{
    [DisallowMultipleComponent]
    public sealed class PlayerStatsRuntime : MonoBehaviour
    {
        private PlayerController playerController;
        private PlayerResources playerResources;
        private ProgressionService service;
        private bool subscribed;
        private bool basesCaptured;

        private float baseRunSpeed;
        private float baseJumpForce;
        private int baseMaxHealth;
        private int baseMaxStamina;

        public float EffectiveRunSpeed { get; private set; }
        public float EffectiveJumpForce { get; private set; }
        public int EffectiveMaxHealth { get; private set; }
        public int EffectiveMaxStamina { get; private set; }

        public float BaseRunSpeed => baseRunSpeed;
        public float BaseJumpForce => baseJumpForce;
        public int BaseMaxHealth => baseMaxHealth;
        public int BaseMaxStamina => baseMaxStamina;

        public event Action OnEffectiveStatsChanged;

        private void OnEnable()
        {
            Subscribe();
            if (basesCaptured)
                Recalculate();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        internal void Bind(
            PlayerController controller,
            PlayerResources resources,
            ProgressionService progressionService)
        {
            Unsubscribe();
            playerController = controller;
            playerResources = resources;
            service = progressionService;
            CaptureBaseValues();
            Subscribe();
            Recalculate();
        }

        public float GetEffectiveRunSpeed(float fallback)
        {
            return basesCaptured ? EffectiveRunSpeed : fallback;
        }

        public float GetEffectiveJumpForce(float fallback)
        {
            return basesCaptured ? EffectiveJumpForce : fallback;
        }

        public float Preview(PlayerStatType stat, AbilityLoadoutDraft draft)
        {
            if (!basesCaptured || service == null)
                return 0f;

            return service.CalculateEffectiveStat(stat, GetBaseValue(stat), draft);
        }

        private void CaptureBaseValues()
        {
            if (basesCaptured || playerController == null || playerResources == null ||
                playerController.stats == null)
            {
                return;
            }

            baseRunSpeed = Mathf.Max(0f, playerController.stats.maxRunSpeed);
            baseJumpForce = Mathf.Max(0f, playerController.stats.jumpForce);
            baseMaxHealth = Mathf.Max(1, playerResources.ConfiguredMaxHealth);
            baseMaxStamina = Mathf.Max(0, playerResources.ConfiguredMaxStamina);
            basesCaptured = true;
        }

        private void Subscribe()
        {
            if (service == null || subscribed)
                return;

            service.OnProgressionChanged += Recalculate;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (service != null && subscribed)
                service.OnProgressionChanged -= Recalculate;
            subscribed = false;
        }

        private void Recalculate()
        {
            if (!basesCaptured || service == null || playerResources == null)
                return;

            EffectiveRunSpeed = service.CalculateEffectiveStat(
                PlayerStatType.MovementSpeed,
                baseRunSpeed);
            EffectiveJumpForce = service.CalculateEffectiveStat(
                PlayerStatType.JumpForce,
                baseJumpForce);
            EffectiveMaxHealth = Mathf.Max(
                1,
                Mathf.RoundToInt(service.CalculateEffectiveStat(
                    PlayerStatType.MaxHealth,
                    baseMaxHealth)));
            EffectiveMaxStamina = Mathf.Max(
                0,
                Mathf.RoundToInt(service.CalculateEffectiveStat(
                    PlayerStatType.MaxStamina,
                    baseMaxStamina)));

            playerResources.ApplyRuntimeMaxHealth(EffectiveMaxHealth);
            playerResources.ApplyRuntimeMaxStamina(EffectiveMaxStamina);
            OnEffectiveStatsChanged?.Invoke();
        }

        private float GetBaseValue(PlayerStatType stat)
        {
            switch (stat)
            {
                case PlayerStatType.MovementSpeed:
                    return baseRunSpeed;
                case PlayerStatType.JumpForce:
                    return baseJumpForce;
                case PlayerStatType.MaxHealth:
                    return baseMaxHealth;
                case PlayerStatType.MaxStamina:
                    return baseMaxStamina;
                default:
                    return 0f;
            }
        }
    }
}
