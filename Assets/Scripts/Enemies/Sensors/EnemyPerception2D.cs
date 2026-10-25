using Game.Events;
using Game.Player;
using UnityEngine;

namespace Game.Enemies
{
    public enum EnemyPerceptionStimulus
    {
        None,
        Vision,
        Noise
    }

    [DisallowMultipleComponent]
    public sealed class EnemyPerception2D : MonoBehaviour
    {
        private const int RaycastCapacity = 16;
        private const int MinimumVisualRays = 3;
        private const int MaximumVisualRays = 64;

        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private string targetTag = "Player";
        [SerializeField] private LayerMask targetMask = 1 << 9;

        [Header("Vision")]
        [SerializeField] private bool visionEnabled = true;
        [SerializeField] private Transform visionOrigin;
        [SerializeField, Min(0.1f)] private float visionDistance = 6f;
        [SerializeField, Range(1f, 179f)] private float visionAngle = 90f;
        [SerializeField] private LayerMask obstacleMask = (1 << 3) | (1 << 6) | (1 << 13);
        [SerializeField, Min(0f)] private float acquisitionTime;
        [SerializeField, Min(0f)] private float lostSightGrace = 0.15f;

        [Header("Hearing")]
        [SerializeField] private bool hearingEnabled;
        [SerializeField] private GameplayNoiseEventChannelSO noiseChannel;
        [SerializeField, Min(0f)] private float maximumHearingDistance = 5f;
        [Tooltip("Scales this enemy's hearing distance, but never extends a sound beyond its source Radius.")]
        [SerializeField, Min(0f)] private float hearingSensitivity = 1f;

        [Header("Runtime Vision Cone")]
        [SerializeField] private bool showConeDuringPlayerStealth = true;
        [SerializeField, Range(MinimumVisualRays, MaximumVisualRays)] private int visualRayCount = 32;
        [SerializeField] private Color coneColor = new Color(1f, 0.72f, 0.12f, 0.16f);
        [SerializeField] private string coneSortingLayer = "Characters";
        [SerializeField] private int coneSortingOrder = -2;

        [Header("Scene Gizmos")]
        [SerializeField] private bool drawSceneGizmos = true;
        [SerializeField] private Color visionGizmoColor = new Color(1f, 0.72f, 0.12f, 0.9f);
        [SerializeField] private Color hearingGizmoColor = new Color(0.85f, 0.2f, 1f, 0.65f);

        [Header("Runtime Diagnostics (read only in Play Mode)")]
        [SerializeField] private bool runtimePlayerVisible;
        [SerializeField] private bool runtimeNoiseActive;
        [SerializeField] private EnemyPerceptionStimulus runtimeLastStimulus;
        [SerializeField] private Vector2 runtimeLastSeenPosition;
        [SerializeField] private Vector2 runtimeLastHeardPosition;
        [SerializeField] private GameplayNoiseAction runtimeLastHeardAction;
        [SerializeField] private float runtimeSecondsSinceLastPerception;

        private readonly RaycastHit2D[] raycastHits = new RaycastHit2D[RaycastCapacity];
        private Transform cachedTarget;
        private Collider2D cachedTargetCollider;
        private PlayerController cachedPlayerController;
        private Vector2 facingDirection = Vector2.right;
        private float targetResolveTimer;
        private float visionAcquireTimer;
        private float lastRawVisibleTime = float.NegativeInfinity;
        private float lastPerceptionTime = float.NegativeInfinity;
        private float audibleStimulusUntil = float.NegativeInfinity;
        private MeshFilter coneMeshFilter;
        private MeshRenderer coneMeshRenderer;
        private Mesh coneMesh;
        private Material coneMaterial;
        private Vector3[] coneVertices;
        private int[] coneTriangles;

        public bool IsPlayerVisible { get; private set; }
        public bool HasActiveNoiseStimulus => Time.time <= audibleStimulusUntil;
        public Vector2 LastSeenPosition { get; private set; }
        public Vector2 LastHeardPosition { get; private set; }
        public GameplayNoiseAction LastHeardAction { get; private set; }
        public EnemyPerceptionStimulus LastStimulus { get; private set; }
        public float TimeSinceLastPerception => float.IsNegativeInfinity(lastPerceptionTime)
            ? float.PositiveInfinity
            : Mathf.Max(0f, Time.time - lastPerceptionTime);
        public Transform Target => cachedTarget;
        public Vector2 TargetPoint => cachedTarget != null ? GetTargetPoint() : Origin;
        public Vector2 Origin => visionOrigin != null ? visionOrigin.position : transform.position;
        public Vector2 FacingDirection => facingDirection;
        public float VisionDistance => visionDistance;
        public float VisionAngle => visionAngle;
        public float AcquisitionTime => acquisitionTime;
        public float LostSightGrace => lostSightGrace;

        public bool IsPathOccluded(Vector2 targetPoint)
        {
            Vector2 offset = targetPoint - Origin;
            float distance = offset.magnitude;
            return distance > 0.0001f &&
                   TryGetNearestObstacle(Origin, offset / distance, distance, out _);
        }

        private void Awake()
        {
            facingDirection = NormalizeDirection(facingDirection);
            ResolveTarget(true);
        }

        private void OnEnable()
        {
            ResolveTarget(true);
            noiseChannel?.Subscribe(HandleNoise);
        }

        private void OnDisable()
        {
            noiseChannel?.Unsubscribe(HandleNoise);
            IsPlayerVisible = false;
            audibleStimulusUntil = float.NegativeInfinity;
            SetConeVisible(false);
        }

        private void OnDestroy()
        {
            if (coneMesh != null)
                Destroy(coneMesh);
            if (coneMaterial != null)
                Destroy(coneMaterial);
        }

        private void Update()
        {
            ResolveTarget(false);
            UpdateVision();
            UpdateRuntimeDiagnostics();
        }

        private void LateUpdate()
        {
            bool showCone = visionEnabled &&
                            showConeDuringPlayerStealth &&
                            cachedPlayerController != null &&
                            cachedPlayerController.IsStealthModeActive;
            SetConeVisible(showCone);
            if (showCone)
                UpdateConeMesh();
        }

        public void SetFacingDirection(Vector2 direction)
        {
            facingDirection = NormalizeDirection(direction);
        }

        public Vector2 GetVisionRayEnd(float signedAngle)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, signedAngle) * facingDirection;
            Vector2 origin = Origin;
            return TryGetNearestObstacle(origin, direction, visionDistance, out RaycastHit2D hit)
                ? hit.point
                : origin + direction * visionDistance;
        }

        private void UpdateVision()
        {
            if (!visionEnabled || cachedTarget == null || !IsTargetLayerAllowed())
            {
                IsPlayerVisible = false;
                visionAcquireTimer = 0f;
                return;
            }

            Vector2 origin = Origin;
            Vector2 targetPoint = GetTargetPoint();
            Vector2 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;
            bool rawVisible = distance <= visionDistance &&
                              (distance <= 0.0001f ||
                               (Vector2.Angle(facingDirection, toTarget) <= visionAngle * 0.5f &&
                                !TryGetNearestObstacle(
                                    origin,
                                    toTarget / distance,
                                    Mathf.Max(0f, distance - 0.01f),
                                    out _)));

            if (rawVisible)
            {
                lastRawVisibleTime = Time.time;
                LastSeenPosition = targetPoint;
                visionAcquireTimer += Time.deltaTime;
                if (visionAcquireTimer >= acquisitionTime)
                    IsPlayerVisible = true;

                if (IsPlayerVisible)
                    RecordPerception(EnemyPerceptionStimulus.Vision);
                return;
            }

            visionAcquireTimer = 0f;
            if (!IsPlayerVisible || Time.time - lastRawVisibleTime > lostSightGrace)
                IsPlayerVisible = false;
        }

        private void HandleNoise(GameplayNoiseEvent noiseEvent)
        {
            if (!hearingEnabled || noiseEvent.Emitter == null || noiseEvent.Intensity <= 0f)
                return;

            ResolveTarget(false);
            if (cachedTarget == null ||
                (noiseEvent.Emitter.transform != cachedTarget &&
                 !noiseEvent.Emitter.transform.IsChildOf(cachedTarget)))
            {
                return;
            }

            float sourceRadius = noiseEvent.Radius > 0f
                ? noiseEvent.Radius
                : maximumHearingDistance * noiseEvent.Intensity;
            float listenerReach = maximumHearingDistance * hearingSensitivity;
            float effectiveRadius = Mathf.Min(sourceRadius, listenerReach);
            if (effectiveRadius <= 0f ||
                ((Vector2)transform.position - noiseEvent.Position).sqrMagnitude >
                effectiveRadius * effectiveRadius)
            {
                return;
            }

            LastHeardPosition = noiseEvent.Position;
            LastHeardAction = noiseEvent.Action;
            float stimulusDuration = noiseEvent.Duration > 0f ? noiseEvent.Duration : 0.15f;
            audibleStimulusUntil = Time.time + stimulusDuration;
            RecordPerception(EnemyPerceptionStimulus.Noise);
        }

        private void RecordPerception(EnemyPerceptionStimulus stimulus)
        {
            LastStimulus = stimulus;
            lastPerceptionTime = Time.time;
        }

        private void UpdateRuntimeDiagnostics()
        {
            runtimePlayerVisible = IsPlayerVisible;
            runtimeNoiseActive = HasActiveNoiseStimulus;
            runtimeLastStimulus = LastStimulus;
            runtimeLastSeenPosition = LastSeenPosition;
            runtimeLastHeardPosition = LastHeardPosition;
            runtimeLastHeardAction = LastHeardAction;
            runtimeSecondsSinceLastPerception = TimeSinceLastPerception;
        }

        private void ResolveTarget(bool immediate)
        {
            if (cachedTarget != null && cachedTarget.gameObject.activeInHierarchy)
                return;
            if (!immediate)
            {
                targetResolveTimer -= Time.deltaTime;
                if (targetResolveTimer > 0f)
                    return;
            }

            targetResolveTimer = 0.5f;
            cachedTarget = target;
            if (cachedTarget == null && !string.IsNullOrEmpty(targetTag))
            {
                GameObject found = GameObject.FindGameObjectWithTag(targetTag);
                if (found != null)
                    cachedTarget = found.transform;
            }

            cachedTargetCollider = cachedTarget != null
                ? cachedTarget.GetComponent<Collider2D>() ??
                  cachedTarget.GetComponentInChildren<Collider2D>(true)
                : null;
            cachedPlayerController = cachedTarget != null
                ? cachedTarget.GetComponent<PlayerController>() ??
                  cachedTarget.GetComponentInChildren<PlayerController>(true)
                : null;
        }

        private Vector2 GetTargetPoint()
        {
            return cachedTargetCollider != null
                ? (Vector2)cachedTargetCollider.bounds.center
                : (Vector2)cachedTarget.position;
        }

        private bool IsTargetLayerAllowed()
        {
            if (cachedTarget == null || targetMask.value == 0)
                return cachedTarget != null;

            int targetLayer = cachedTargetCollider != null
                ? cachedTargetCollider.gameObject.layer
                : cachedTarget.gameObject.layer;
            return (targetMask.value & (1 << targetLayer)) != 0;
        }

        private bool TryGetNearestObstacle(
            Vector2 origin,
            Vector2 direction,
            float distance,
            out RaycastHit2D nearest)
        {
            nearest = default;
            if (obstacleMask.value == 0 || distance <= 0f)
                return false;

            int count = Physics2D.RaycastNonAlloc(
                origin,
                direction,
                raycastHits,
                distance,
                obstacleMask);
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D candidate = raycastHits[i];
                if (candidate.collider == null || candidate.distance >= nearestDistance)
                    continue;

                nearest = candidate;
                nearestDistance = candidate.distance;
            }

            return nearest.collider != null;
        }

        private void SetConeVisible(bool visible)
        {
            if (!visible && coneMeshRenderer == null)
                return;
            if (visible)
                EnsureConeResources();
            if (coneMeshRenderer != null)
                coneMeshRenderer.enabled = visible;
        }

        private void EnsureConeResources()
        {
            int rays = Mathf.Clamp(visualRayCount, MinimumVisualRays, MaximumVisualRays);
            if (coneMeshRenderer == null)
            {
                GameObject visual = new GameObject("PerceptionVisionCone");
                visual.transform.SetParent(transform, false);
                coneMeshFilter = visual.AddComponent<MeshFilter>();
                coneMeshRenderer = visual.AddComponent<MeshRenderer>();
                coneMeshRenderer.sortingLayerName = coneSortingLayer;
                coneMeshRenderer.sortingOrder = coneSortingOrder;
            }

            if (coneMesh == null)
            {
                coneMesh = new Mesh
                {
                    name = "Runtime Enemy Perception Cone",
                    hideFlags = HideFlags.DontSave
                };
                coneMeshFilter.sharedMesh = coneMesh;
            }

            if (coneMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    coneMaterial = new Material(shader)
                    {
                        name = "Runtime Enemy Perception Cone",
                        hideFlags = HideFlags.DontSave,
                        color = coneColor
                    };
                    coneMeshRenderer.sharedMaterial = coneMaterial;
                }
            }

            int vertexCount = rays + 2;
            if (coneVertices == null || coneVertices.Length != vertexCount)
            {
                coneVertices = new Vector3[vertexCount];
                coneTriangles = new int[rays * 3];
                for (int i = 0; i < rays; i++)
                {
                    int triangle = i * 3;
                    coneTriangles[triangle] = 0;
                    coneTriangles[triangle + 1] = i + 1;
                    coneTriangles[triangle + 2] = i + 2;
                }
            }
        }

        private void UpdateConeMesh()
        {
            EnsureConeResources();
            if (coneMesh == null || coneVertices == null)
                return;

            int rays = coneVertices.Length - 2;
            Vector2 origin = Origin;
            coneVertices[0] = transform.InverseTransformPoint(origin);
            float halfAngle = visionAngle * 0.5f;
            for (int i = 0; i <= rays; i++)
            {
                float signedAngle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)rays);
                coneVertices[i + 1] = transform.InverseTransformPoint(GetVisionRayEnd(signedAngle));
            }

            if (coneMaterial != null)
                coneMaterial.color = coneColor;
            coneMesh.Clear();
            coneMesh.vertices = coneVertices;
            coneMesh.triangles = coneTriangles;
            coneMesh.RecalculateBounds();
        }

        private static Vector2 NormalizeDirection(Vector2 direction)
        {
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        }

        private void OnValidate()
        {
            visionDistance = Mathf.Max(0.1f, visionDistance);
            visionAngle = Mathf.Clamp(visionAngle, 1f, 179f);
            acquisitionTime = Mathf.Max(0f, acquisitionTime);
            lostSightGrace = Mathf.Max(0f, lostSightGrace);
            maximumHearingDistance = Mathf.Max(0f, maximumHearingDistance);
            hearingSensitivity = Mathf.Max(0f, hearingSensitivity);
            visualRayCount = Mathf.Clamp(visualRayCount, MinimumVisualRays, MaximumVisualRays);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawSceneGizmos)
                return;

            Vector2 origin = visionOrigin != null ? visionOrigin.position : transform.position;
            Vector2 direction = NormalizeDirection(facingDirection);
            if (visionEnabled)
            {
                float halfAngle = visionAngle * 0.5f;
                Vector2 left = Quaternion.Euler(0f, 0f, -halfAngle) * direction;
                Vector2 right = Quaternion.Euler(0f, 0f, halfAngle) * direction;
                Gizmos.color = visionGizmoColor;
                Gizmos.DrawLine(origin, origin + left * visionDistance);
                Gizmos.DrawLine(origin, origin + direction * visionDistance);
                Gizmos.DrawLine(origin, origin + right * visionDistance);
                const int arcSegments = 20;
                Vector2 previous = origin + left * visionDistance;
                for (int i = 1; i <= arcSegments; i++)
                {
                    float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)arcSegments);
                    Vector2 next = origin +
                                   (Vector2)(Quaternion.Euler(0f, 0f, angle) * direction) *
                                   visionDistance;
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
            }

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(origin, 0.08f);
            if (hearingEnabled)
            {
                Gizmos.color = hearingGizmoColor;
                Gizmos.DrawWireSphere(transform.position, maximumHearingDistance);
            }
        }
#endif
    }
}
