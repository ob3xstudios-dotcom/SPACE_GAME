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
        [SerializeField, Range(0f, 1f)] private float walkFootstepIntensity = 0.25f;
        [SerializeField, Range(0f, 1f)] private float runFootstepIntensity = 0.55f;

        [Header("Landing")]
        [SerializeField, Range(0f, 1f)] private float hardLandingIntensity = 1f;

        [Header("Fast Movement")]
        [SerializeField, Range(0f, 1f)] private float dashIntensity = 0.85f;
        [SerializeField, Range(0f, 1f)] private float slideIntensity = 0.8f;
        [SerializeField, Range(0f, 1f)] private float diveIntensity = 1f;

        [Header("Combat")]
        [SerializeField, Range(0f, 1f)] private float attackImpactIntensity = 0.75f;
        [SerializeField, Range(0f, 1f)] private float parryIntensity = 0.9f;

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

            Emit(position, intensity, GameplayNoiseAction.Footstep);
        }

        public void EmitHardLanding(Vector2 position)
        {
            Emit(position, hardLandingIntensity, GameplayNoiseAction.HardLanding);
        }

        public void EmitDash(Vector2 position)
        {
            Emit(position, dashIntensity, GameplayNoiseAction.Dash);
        }

        public void EmitSlide(Vector2 position)
        {
            Emit(position, slideIntensity, GameplayNoiseAction.Slide);
        }

        public void EmitDive(Vector2 position)
        {
            Emit(position, diveIntensity, GameplayNoiseAction.Dive);
        }

        public void EmitAttackImpact(Vector2 position)
        {
            Emit(position, attackImpactIntensity, GameplayNoiseAction.AttackImpact);
        }

        public void EmitParry(Vector2 position)
        {
            Emit(position, parryIntensity, GameplayNoiseAction.Parry);
        }

        private void Emit(Vector2 position, float intensity, GameplayNoiseAction action)
        {
            if (!isActiveAndEnabled || noiseChannel == null || intensity <= 0f)
                return;

            noiseChannel.Raise(position, intensity, action, gameObject);
        }

        private void ResolveController()
        {
            if (controller == null)
                controller = GetComponent<PlayerController>();
        }

        private void OnValidate()
        {
            stealthFootstepIntensity = Mathf.Clamp01(stealthFootstepIntensity);
            walkFootstepIntensity = Mathf.Clamp01(walkFootstepIntensity);
            runFootstepIntensity = Mathf.Clamp01(runFootstepIntensity);
            hardLandingIntensity = Mathf.Clamp01(hardLandingIntensity);
            dashIntensity = Mathf.Clamp01(dashIntensity);
            slideIntensity = Mathf.Clamp01(slideIntensity);
            diveIntensity = Mathf.Clamp01(diveIntensity);
            attackImpactIntensity = Mathf.Clamp01(attackImpactIntensity);
            parryIntensity = Mathf.Clamp01(parryIntensity);
            ResolveController();
        }
    }
}
