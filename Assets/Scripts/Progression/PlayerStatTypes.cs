using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Progression
{
    public enum PlayerStatType
    {
        MovementSpeed,
        JumpForce,
        MaxHealth,
        MaxStamina
    }

    [Serializable]
    public struct PlayerStatModifier
    {
        public PlayerStatType stat;
        public float flatAmount;
        [Tooltip("Decimal percentage. 0.05 = +5%, -0.10 = -10%.")]
        public float percentAmount;

        public PlayerStatModifier(PlayerStatType stat, float flatAmount, float percentAmount)
        {
            this.stat = stat;
            this.flatAmount = flatAmount;
            this.percentAmount = percentAmount;
        }
    }

    public static class PlayerStatMath
    {
        public static float Calculate(
            float baseValue,
            PlayerStatType stat,
            IEnumerable<PlayerStatModifier> permanentModifiers,
            IEnumerable<PlayerStatModifier> temporaryModifiers)
        {
            Accumulate(stat, permanentModifiers, out float permanentFlat, out float permanentPercent);
            Accumulate(stat, temporaryModifiers, out float temporaryFlat, out float temporaryPercent);

            float permanentValue =
                Mathf.Max(0f, baseValue + permanentFlat) *
                Mathf.Max(0f, 1f + permanentPercent);
            return
                Mathf.Max(0f, permanentValue + temporaryFlat) *
                Mathf.Max(0f, 1f + temporaryPercent);
        }

        private static void Accumulate(
            PlayerStatType stat,
            IEnumerable<PlayerStatModifier> modifiers,
            out float flat,
            out float percent)
        {
            flat = 0f;
            percent = 0f;
            if (modifiers == null)
                return;

            foreach (PlayerStatModifier modifier in modifiers)
            {
                if (modifier.stat != stat)
                    continue;

                flat += modifier.flatAmount;
                percent += modifier.percentAmount;
            }
        }
    }
}
