using Cinemachine;
using Game.Input;
using Game.Player;
using UnityEngine;

namespace Game.Systems
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineVirtualCamera))]
    public class CameraLookAheadTarget2D : MonoBehaviour
    {
        [Header("Manual Look")]
        [SerializeField] private Vector2 maximumOffset = new Vector2(2.5f, 1.5f);
        [SerializeField, Range(0f, 0.9f)] private float inputDeadzone = 0.15f;
        [SerializeField, Range(1f, 30f)] private float lookSharpness = 8f;
        [SerializeField, Range(1f, 30f)] private float returnSharpness = 5f;

        private CinemachineVirtualCamera virtualCamera;
        private CinemachineFramingTransposer framingTransposer;
        private CinemachineBrain brain;
        private Transform inputSource;
        private InputReader input;
        private PlayerController player;
        private Vector3 baseTrackedObjectOffset;
        private Vector2 currentManualOffset;

        private void Reset()
        {
            maximumOffset = new Vector2(2.5f, 1.5f);
            inputDeadzone = 0.15f;
            lookSharpness = 8f;
            returnSharpness = 5f;
        }

        private void OnEnable()
        {
            virtualCamera = GetComponent<CinemachineVirtualCamera>();
            ResolveDependencies(true);
        }

        private void LateUpdate()
        {
            ResolveDependencies(false);
            if (framingTransposer == null)
                return;

            bool cameraHasPriority =
                virtualCamera != null &&
                virtualCamera.isActiveAndEnabled &&
                CinemachineCore.Instance.IsLive(virtualCamera) &&
                (brain == null || !brain.IsBlending);
            bool playerAllowsLook = player == null || !player.IsExternalControlLocked;
            Vector2 desiredOffset = cameraHasPriority && playerAllowsLook
                ? CalculateDesiredOffset()
                : Vector2.zero;
            float sharpness = desiredOffset.sqrMagnitude > 0.0001f
                ? lookSharpness
                : returnSharpness;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * Time.deltaTime);
            currentManualOffset = Vector2.Lerp(currentManualOffset, desiredOffset, blend);

            framingTransposer.m_TrackedObjectOffset = baseTrackedObjectOffset +
                new Vector3(currentManualOffset.x, currentManualOffset.y, 0f);
        }

        private void OnDisable()
        {
            if (framingTransposer != null)
                framingTransposer.m_TrackedObjectOffset = baseTrackedObjectOffset;

            currentManualOffset = Vector2.zero;
        }

        private void ResolveDependencies(bool force)
        {
            if (virtualCamera == null)
                virtualCamera = GetComponent<CinemachineVirtualCamera>();

            if (brain == null && Camera.main != null)
                brain = Camera.main.GetComponent<CinemachineBrain>();

            CinemachineFramingTransposer resolvedFraming = virtualCamera != null
                ? virtualCamera.GetCinemachineComponent<CinemachineFramingTransposer>()
                : null;
            if (force || resolvedFraming != framingTransposer)
            {
                framingTransposer = resolvedFraming;
                if (framingTransposer != null)
                    baseTrackedObjectOffset = framingTransposer.m_TrackedObjectOffset;
            }

            Transform resolvedSource = virtualCamera != null ? virtualCamera.Follow : null;
            if (!force && resolvedSource == inputSource)
                return;

            inputSource = resolvedSource;
            input = inputSource != null
                ? inputSource.GetComponentInParent<InputReader>()
                : null;
            player = inputSource != null
                ? inputSource.GetComponentInParent<PlayerController>()
                : null;
        }

        private Vector2 CalculateDesiredOffset()
        {
            if (input == null)
                return Vector2.zero;

            Vector2 look = Vector2.ClampMagnitude(input.CameraLookInput, 1f);
            float magnitude = look.magnitude;
            float deadzone = Mathf.Clamp(inputDeadzone, 0f, 0.9f);
            if (magnitude <= deadzone)
                return Vector2.zero;

            float scaledMagnitude = Mathf.InverseLerp(deadzone, 1f, magnitude);
            Vector2 scaledLook = look / magnitude * scaledMagnitude;
            return new Vector2(
                scaledLook.x * Mathf.Max(0f, maximumOffset.x),
                scaledLook.y * Mathf.Max(0f, maximumOffset.y));
        }
    }
}
