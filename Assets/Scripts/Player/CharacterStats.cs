using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(
        fileName = "NewCharacterStats",
        menuName = "Characters/Character Stats")]
    public class CharacterStats : ScriptableObject
    {
        [Header("Movimiento - Game Feel")]
        [Tooltip("Velocidad máxima horizontal.")]
        [Min(0f)]
        public float maxRunSpeed = 7f;

        [Tooltip("Aceleración en suelo (más alto = responde más rápido).")]
        [Min(0f)]
        public float groundAcceleration = 90f;

        [Tooltip("Frenada en suelo cuando no hay input (más alto = se para antes).")]
        [Min(0f)]
        public float groundDeceleration = 110f;

        [Tooltip("Aceleración en aire (más bajo = más inercia en aire).")]
        [Min(0f)]
        public float airAcceleration = 55f;

        [Tooltip("Frenada en aire cuando no hay input.")]
        [Min(0f)]
        public float airDeceleration = 35f;

        [Tooltip("Multiplicador extra al cambiar de dirección (giro más snappy).")]
        [Range(1f, 3f)]
        public float turnAccelerationMultiplier = 1.35f;

        [Header("Salto - Fuerza base")]
        [Tooltip("Impulso vertical del salto.")]
        public float jumpForce = 14f;

        [Tooltip("Gravedad base del Rigidbody2D.")]
        [Min(0f)]
        public float gravityScale = 4f;

        [Header("Salto - Game Feel")]
        [Tooltip("Multiplicador de gravedad al caer (más alto = caes más rápido).")]
        [Min(1f)]
        public float fallGravityMultiplier = 2.2f;

        [Tooltip("Multiplicador cerca del apex (más bajo = flota un poco).")]
        [Range(0.2f, 2f)]
        public float apexGravityMultiplier = 0.9f;

        [Tooltip("Si |velY| < este valor, consideramos apex.")]
        [Min(0f)]
        public float apexThreshold = 1.0f;

        [Tooltip("Recorte del salto al soltar el botón. 1 = no recorta, 0.5 = recorta bastante.")]
        [Range(0.1f, 1f)]
        public float jumpCutMultiplier = 0.5f;

        [Header("Límites")]
        [Tooltip("Velocidad máxima de caída (negativo). Ej: -25.")]
        public float maxFallSpeed = -25f;

        [Header("Salto - Ventanas (HK feel)")]
        [Tooltip("Tiempo extra para saltar tras salir del borde.")]
        [Range(0f, 0.25f)]
        public float coyoteTime = 0.10f;

        [Tooltip("Tiempo que guardamos un salto pulsado antes de tocar suelo.")]
        [Range(0f, 0.25f)]
        public float jumpBufferTime = 0.10f;

        [Header("Dash")]
        [Tooltip("¿Este personaje tiene dash? (para kits que no lo tengan).")]
        public bool hasDash = true;

        [Tooltip("Velocidad del dash.")]
        [Min(0f)]
        public float dashSpeed = 18f;

        [Tooltip("Duración del dash en segundos.")]
        [Range(0.02f, 0.5f)]
        public float dashDuration = 0.12f;

        [Tooltip("Cooldown tras un dash.")]
        [Range(0f, 1.5f)]
        public float dashCooldown = 0.25f;

        [Tooltip("Permite dash en el aire.")]
        public bool allowAirDash = true;

        [Tooltip("Si allowAirDash = true, cuántos dashes en el aire antes de tocar suelo.")]
        [Min(0)]
        public int airDashesMax = 1;

        [Header("Wall")]
        [Tooltip("Velocidad máxima al deslizar por pared (negativo). Ej: -2.")]
        public float wallSlideSpeed = -2f;

        [Tooltip("Fuerza horizontal del wall jump.")]
        public float wallJumpForceX = 12f;

        [Tooltip("Fuerza vertical del wall jump.")]
        public float wallJumpForceY = 14f;

        [Tooltip("Tiempo durante el cual se bloquea el input tras wall jump (evita 'pegarse' a la pared).")]
        [Range(0f, 0.4f)]
        public float wallJumpLockTime = 0.15f;

        public int attackDamage = 1;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Seguridad: maxFallSpeed debe ser negativo
            if (maxFallSpeed > 0f) maxFallSpeed = -Mathf.Abs(maxFallSpeed);

            // Seguridad: wallSlideSpeed normalmente es negativo
            if (wallSlideSpeed > 0f) wallSlideSpeed = -Mathf.Abs(wallSlideSpeed);

            // Seguridad: airDashesMax no negativo
            if (airDashesMax < 0) airDashesMax = 0;
        }
#endif
    }
}