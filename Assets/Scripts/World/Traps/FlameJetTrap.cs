using System.Collections;
using UnityEngine;

namespace Game.World.Traps
{
    public enum FlameJetMode
    {
        Continuous,
        Intermittent
    }

    /// <summary>
    /// Owns the timing and dimensions of a reusable flame jet.
    /// The jet points along this transform's local +X axis.
    /// </summary>
    public sealed class FlameJetTrap : MonoBehaviour
    {
        private enum FlameJetState
        {
            Off,
            Warning,
            Emitting
        }

        [Header("Cycle")]
        [SerializeField] private FlameJetMode mode = FlameJetMode.Intermittent;
        [SerializeField, Min(0f)] private float offTime = 2f;
        [SerializeField, Min(0f)] private float warningTime = 0.5f;
        [SerializeField, Min(0.05f)] private float emissionTime = 1.5f;

        [Header("Jet")]
        [SerializeField, Min(0.1f)] private float jetLength = 4f;
        [SerializeField, Min(0.1f)] private float jetWidth = 1f;
        [SerializeField, Min(1)] private int damage = 1;
        [Tooltip("Optional collision-layer filter. Nothing (0) means PlayerHealth is the only filter.")]
        [SerializeField] private LayerMask targetLayers;

        [Header("Parts")]
        [SerializeField] private FlameJetDamageZone damageZone;
        [SerializeField] private FlameJetVisual visual;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        private Coroutine cycleRoutine;
        private FlameJetState state;

        public bool IsEmitting => state == FlameJetState.Emitting;

        private void Awake()
        {
            ResolveReferences();
            ApplyConfiguration();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ApplyConfiguration();

            if (mode == FlameJetMode.Continuous)
            {
                SetState(FlameJetState.Emitting);
                return;
            }

            cycleRoutine = StartCoroutine(IntermittentCycle());
        }

        private void OnDisable()
        {
            if (cycleRoutine != null)
            {
                StopCoroutine(cycleRoutine);
                cycleRoutine = null;
            }

            SetState(FlameJetState.Off);
        }

        private IEnumerator IntermittentCycle()
        {
            while (true)
            {
                SetState(FlameJetState.Off);
                if (offTime > 0f)
                    yield return new WaitForSeconds(offTime);
                else
                    yield return null;

                SetState(FlameJetState.Warning);
                if (warningTime > 0f)
                    yield return new WaitForSeconds(warningTime);
                else
                    yield return null;

                SetState(FlameJetState.Emitting);
                yield return new WaitForSeconds(Mathf.Max(0.05f, emissionTime));
            }
        }

        private void SetState(FlameJetState nextState)
        {
            state = nextState;
            bool isWarning = state == FlameJetState.Warning;
            bool isEmitting = state == FlameJetState.Emitting;

            // Disable damage before hiding the flames; enable it only after flames are visible.
            if (!isEmitting && damageZone != null)
                damageZone.SetEmissionActive(false);

            if (visual != null)
                visual.SetState(isWarning, isEmitting);

            if (isEmitting && damageZone != null)
                damageZone.SetEmissionActive(true);

            if (debugLogs)
                Debug.Log($"[FLAME JET] {name}: {state}", this);
        }

        private void ResolveReferences()
        {
            if (damageZone == null)
                damageZone = GetComponentInChildren<FlameJetDamageZone>(true);

            if (visual == null)
                visual = GetComponentInChildren<FlameJetVisual>(true);
        }

        private void ApplyConfiguration()
        {
            jetLength = Mathf.Max(0.1f, jetLength);
            jetWidth = Mathf.Max(0.1f, jetWidth);
            damage = Mathf.Max(1, damage);

            if (damageZone != null)
                damageZone.Configure(transform, damage, targetLayers, jetLength, jetWidth, debugLogs);

            if (visual != null)
                visual.Configure(jetLength, jetWidth);
        }

        private void OnValidate()
        {
            offTime = Mathf.Max(0f, offTime);
            warningTime = Mathf.Max(0f, warningTime);
            emissionTime = Mathf.Max(0.05f, emissionTime);
            jetLength = Mathf.Max(0.1f, jetLength);
            jetWidth = Mathf.Max(0.1f, jetWidth);
            damage = Mathf.Max(1, damage);

            ResolveReferences();
            ApplyConfiguration();
        }

        private void OnDrawGizmosSelected()
        {
            float length = Mathf.Max(0.1f, jetLength);
            float width = Mathf.Max(0.1f, jetWidth);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = new Color(1f, 0.25f, 0.05f, 0.18f);
            Gizmos.DrawCube(new Vector3(length * 0.5f, 0f, 0f), new Vector3(length, width, 0.02f));
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.95f);
            Gizmos.DrawWireCube(new Vector3(length * 0.5f, 0f, 0f), new Vector3(length, width, 0.02f));

            float arrowSize = Mathf.Min(0.35f, length * 0.2f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(length, 0f, 0f));
            Gizmos.DrawLine(new Vector3(length, 0f, 0f), new Vector3(length - arrowSize, arrowSize, 0f));
            Gizmos.DrawLine(new Vector3(length, 0f, 0f), new Vector3(length - arrowSize, -arrowSize, 0f));

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
