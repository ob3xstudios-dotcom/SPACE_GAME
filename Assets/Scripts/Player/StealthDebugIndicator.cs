using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class StealthDebugIndicator : MonoBehaviour
    {
        [SerializeField] private bool showStealthDebugIndicator = true;
        [SerializeField, Range(0.05f, 0.5f)] private float visualDetectionHoldSeconds = 0.2f;
        [SerializeField] private Vector2 screenPosition = new Vector2(16f, 16f);
        [SerializeField] private Vector2 panelSize = new Vector2(180f, 32f);

        private static readonly Color OffColor = new Color(0.35f, 0.35f, 0.35f, 0.95f);
        private static readonly Color SafeColor = new Color(0.1f, 0.65f, 0.2f, 0.95f);
        private static readonly Color SeenColor = new Color(0.8f, 0.12f, 0.12f, 0.95f);

        private PlayerController playerController;
        private float lastVisualDetectionTime = float.NegativeInfinity;
        private GUIStyle indicatorStyle;

        public bool IsPlayerCurrentlySeen =>
            Time.unscaledTime - lastVisualDetectionTime <=
            Mathf.Max(0.05f, visualDetectionHoldSeconds);

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
        }

        public void ReportVisualDetection()
        {
            lastVisualDetectionTime = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (!showStealthDebugIndicator || playerController == null)
                return;

            bool stealthActive = playerController.IsStealthModeActive;
            string label = !stealthActive
                ? "STEALTH OFF"
                : IsPlayerCurrentlySeen
                    ? "STEALTH SEEN"
                    : "STEALTH SAFE";
            Color stateColor = !stealthActive
                ? OffColor
                : IsPlayerCurrentlySeen
                    ? SeenColor
                    : SafeColor;

            EnsureStyle();
            Color previousBackgroundColor = GUI.backgroundColor;
            Color previousContentColor = GUI.contentColor;
            GUI.backgroundColor = stateColor;
            GUI.contentColor = Color.white;
            GUI.Box(
                new Rect(screenPosition.x, screenPosition.y, panelSize.x, panelSize.y),
                label,
                indicatorStyle);
            GUI.backgroundColor = previousBackgroundColor;
            GUI.contentColor = previousContentColor;
        }

        private void EnsureStyle()
        {
            if (indicatorStyle != null)
                return;

            indicatorStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
        }

        private void OnValidate()
        {
            visualDetectionHoldSeconds = Mathf.Clamp(visualDetectionHoldSeconds, 0.05f, 0.5f);
            panelSize.x = Mathf.Max(1f, panelSize.x);
            panelSize.y = Mathf.Max(1f, panelSize.y);
        }
    }
}
