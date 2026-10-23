using Game.Events;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerGameplayNoiseEmitter2D : MonoBehaviour
    {
        [Header("Channel")]
        [SerializeField] private GameplayNoiseEventChannelSO noiseChannel;

        [Header("Footsteps")]
        [SerializeField, Range(0f, 1f)] private float stealthFootstepIntensity;
        [SerializeField, Min(0f)] private float stealthFootstepRadius = 1.25f;
        [SerializeField, Range(0f, 1f)] private float walkFootstepIntensity = 0.25f;
        [SerializeField, Min(0f)] private float walkFootstepRadius = 3f;
        [SerializeField, Range(0f, 1f)] private float runFootstepIntensity = 0.55f;
        [SerializeField, Min(0f)] private float runFootstepRadius = 6f;
        [SerializeField, Min(0f)] private float footstepDuration = 0.35f;

        [Header("Jump")]
        [Tooltip("Normal jumps are silent by default. Raise this explicitly to make them audible.")]
        [SerializeField, Range(0f, 1f)] private float jumpIntensity;
        [SerializeField, Min(0f)] private float jumpRadius = 3.5f;
        [SerializeField, Range(0f, 1f)] private float wallJumpIntensity = 0.45f;
        [SerializeField, Min(0f)] private float wallJumpRadius = 4f;
        [SerializeField, Min(0f)] private float jumpDuration = 0.4f;

        [Header("Landing")]
        [Tooltip("Soft landings are silent by default. Hard landings use their separate profile.")]
        [SerializeField, Range(0f, 1f)] private float landingIntensity;
        [SerializeField, Min(0f)] private float landingRadius = 3f;
        [SerializeField, Range(0f, 1f)] private float hardLandingIntensity = 1f;
        [SerializeField, Min(0f)] private float hardLandingRadius = 8f;
        [SerializeField, Min(0f)] private float landingDuration = 0.45f;

        [Header("Fast Movement")]
        [SerializeField, Range(0f, 1f)] private float dashIntensity = 0.85f;
        [SerializeField, Min(0f)] private float dashRadius = 7f;
        [SerializeField, Range(0f, 1f)] private float slideIntensity = 0.8f;
        [SerializeField, Min(0f)] private float slideRadius = 6f;
        [SerializeField, Range(0f, 1f)] private float diveIntensity = 1f;
        [SerializeField, Min(0f)] private float diveRadius = 8f;
        [SerializeField, Min(0f)] private float fastMovementDuration = 0.5f;

        [Header("Combat")]
        [SerializeField, Range(0f, 1f)] private float attackImpactIntensity = 0.75f;
        [SerializeField, Min(0f)] private float attackImpactRadius = 6f;
        [SerializeField, Range(0f, 1f)] private float parryIntensity = 0.9f;
        [SerializeField, Min(0f)] private float parryRadius = 7f;
        [SerializeField, Min(0f)] private float combatDuration = 0.55f;

        [SerializeField, HideInInspector] private PlayerController controller;

        private void Awake()
        {
            ResolveController();
        }

        public void EmitFootstep(bool running, Vector2 position)
        {
            ResolveController();
            float intensity = controller != null && controller.IsStealthModeActive
                ? stealthFootstepIntensity
                : running
                    ? runFootstepIntensity
                    : walkFootstepIntensity;
            float radius = controller != null && controller.IsStealthModeActive
                ? stealthFootstepRadius
                : running
                    ? runFootstepRadius
                    : walkFootstepRadius;

            Emit(position, intensity, radius, footstepDuration, GameplayNoiseAction.Footstep);
        }

        public void EmitJump(Vector2 position)
        {
            Emit(position, jumpIntensity, jumpRadius, jumpDuration, GameplayNoiseAction.Jump);
        }

        public void EmitWallJump(Vector2 position)
        {
            Emit(position, wallJumpIntensity, wallJumpRadius, jumpDuration, GameplayNoiseAction.WallJump);
        }

        public void EmitLanding(Vector2 position)
        {
            Emit(position, landingIntensity, landingRadius, landingDuration, GameplayNoiseAction.Landing);
        }

        public void EmitHardLanding(Vector2 position)
        {
            Emit(position, hardLandingIntensity, hardLandingRadius, landingDuration, GameplayNoiseAction.HardLanding);
        }

        public void EmitDash(Vector2 position)
        {
            Emit(position, dashIntensity, dashRadius, fastMovementDuration, GameplayNoiseAction.Dash);
        }

        public void EmitSlide(Vector2 position)
        {
            Emit(position, slideIntensity, slideRadius, fastMovementDuration, GameplayNoiseAction.Slide);
        }

        public void EmitDive(Vector2 position)
        {
            Emit(position, diveIntensity, diveRadius, fastMovementDuration, GameplayNoiseAction.Dive);
        }

        public void EmitAttackImpact(Vector2 position)
        {
            Emit(position, attackImpactIntensity, attackImpactRadius, combatDuration, GameplayNoiseAction.AttackImpact);
        }

        public void EmitParry(Vector2 position)
        {
            Emit(position, parryIntensity, parryRadius, combatDuration, GameplayNoiseAction.Parry);
        }

        private void Emit(
            Vector2 position,
            float intensity,
            float radius,
            float duration,
            GameplayNoiseAction action)
        {
            if (!isActiveAndEnabled || noiseChannel == null || intensity <= 0f || radius <= 0f)
                return;

            noiseChannel.Raise(position, intensity, radius, duration, action, gameObject);
        }

        private void ResolveController()
        {
            if (controller == null)
                controller = GetComponent<PlayerController>();
        }

        private void OnValidate()
        {
            stealthFootstepIntensity = Mathf.Clamp01(stealthFootstepIntensity);
            stealthFootstepRadius = Mathf.Max(0f, stealthFootstepRadius);
            walkFootstepIntensity = Mathf.Clamp01(walkFootstepIntensity);
            walkFootstepRadius = Mathf.Max(0f, walkFootstepRadius);
            runFootstepIntensity = Mathf.Clamp01(runFootstepIntensity);
            runFootstepRadius = Mathf.Max(0f, runFootstepRadius);
            footstepDuration = Mathf.Max(0f, footstepDuration);
            jumpIntensity = Mathf.Clamp01(jumpIntensity);
            jumpRadius = Mathf.Max(0f, jumpRadius);
            wallJumpIntensity = Mathf.Clamp01(wallJumpIntensity);
            wallJumpRadius = Mathf.Max(0f, wallJumpRadius);
            jumpDuration = Mathf.Max(0f, jumpDuration);
            landingIntensity = Mathf.Clamp01(landingIntensity);
            landingRadius = Mathf.Max(0f, landingRadius);
            hardLandingIntensity = Mathf.Clamp01(hardLandingIntensity);
            hardLandingRadius = Mathf.Max(0f, hardLandingRadius);
            landingDuration = Mathf.Max(0f, landingDuration);
            dashIntensity = Mathf.Clamp01(dashIntensity);
            dashRadius = Mathf.Max(0f, dashRadius);
            slideIntensity = Mathf.Clamp01(slideIntensity);
            slideRadius = Mathf.Max(0f, slideRadius);
            diveIntensity = Mathf.Clamp01(diveIntensity);
            diveRadius = Mathf.Max(0f, diveRadius);
            fastMovementDuration = Mathf.Max(0f, fastMovementDuration);
            attackImpactIntensity = Mathf.Clamp01(attackImpactIntensity);
            attackImpactRadius = Mathf.Max(0f, attackImpactRadius);
            parryIntensity = Mathf.Clamp01(parryIntensity);
            parryRadius = Mathf.Max(0f, parryRadius);
            combatDuration = Mathf.Max(0f, combatDuration);
            ResolveController();
        }
    }
}
