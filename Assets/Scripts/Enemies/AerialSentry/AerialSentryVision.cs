using UnityEngine;

namespace Game.Enemies.AerialSentry
{
    public enum AerialSentrySightFailure
    {
        None,
        NoPlayer,
        OutOfRange,
        OutsideAngle,
        Occluded
    }

    public sealed class AerialSentryVision : MonoBehaviour
    {
        private const int RaycastCapacity = 16;

        [Header("Vision")]
        [SerializeField] private Transform eyeOrigin;
        [SerializeField, Min(0.1f)] private float visionRange = 7f;
        [SerializeField, Range(1f, 179f)] private float visionAngle = 50f;
        [SerializeField] private LayerMask visionOcclusionMask = 10312;
        [SerializeField] private LayerMask playerMask = 512;
        [SerializeField, Min(0f)] private float detectionAcquireTime = 0.15f;
        [SerializeField, Min(0f)] private float lostSightGrace = 0.15f;
        [SerializeField, Min(1f)] private float visionRotationSpeed = 360f;

        private readonly RaycastHit2D[] raycastHits = new RaycastHit2D[RaycastCapacity];
        private Transform playerTransform;
        private Collider2D playerCollider;
        private float playerResolveTimer;
        private Vector2 viewDirection = Vector2.down;

        public Vector2 Origin => eyeOrigin != null ? eyeOrigin.position : transform.position;
        public float VisionRange => visionRange;
        public float VisionAngle => visionAngle;
        public LayerMask OcclusionMask => visionOcclusionMask;
        public float DetectionAcquireTime => detectionAcquireTime;
        public float LostSightGrace => lostSightGrace;
        public float VisionRotationSpeed => visionRotationSpeed;
        public Transform PlayerTransform => playerTransform;
        public Vector2 ViewDirection => viewDirection;

        private void Awake()
        {
            if (eyeOrigin == null)
                eyeOrigin = transform;

            viewDirection = NormalizeDirection(viewDirection);
            AttemptResolvePlayer();
        }

        private void Update()
        {
            if (playerTransform != null)
                return;

            playerResolveTimer = Mathf.Max(0f, playerResolveTimer - Time.deltaTime);
        }

        public bool TryGetVisibleTarget(out Vector2 targetPoint, out AerialSentrySightFailure failure)
        {
            targetPoint = default;

            if (playerTransform == null && playerResolveTimer <= 0f)
                AttemptResolvePlayer();

            if (playerTransform == null)
            {
                failure = AerialSentrySightFailure.NoPlayer;
                return false;
            }

            targetPoint = GetPlayerTargetPoint();
            Vector2 toPlayer = targetPoint - Origin;
            float distance = toPlayer.magnitude;

            if (distance > visionRange)
            {
                failure = AerialSentrySightFailure.OutOfRange;
                return false;
            }

            if (distance > 0.0001f && Vector2.Angle(viewDirection, toPlayer) > visionAngle * 0.5f)
            {
                failure = AerialSentrySightFailure.OutsideAngle;
                return false;
            }

            if (distance > 0.0001f && TryGetNearestOcclusion(Origin, toPlayer / distance, distance, out _))
            {
                failure = AerialSentrySightFailure.Occluded;
                return false;
            }

            failure = AerialSentrySightFailure.None;
            return true;
        }

        public bool IsPathOccluded(Vector2 targetPoint)
        {
            Vector2 toTarget = targetPoint - Origin;
            float distance = toTarget.magnitude;
            return distance > 0.0001f &&
                   TryGetNearestOcclusion(Origin, toTarget / distance, distance, out _);
        }

        public Vector2 GetOccludedRayEnd(float signedAngle)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, signedAngle) * viewDirection;
            if (TryGetNearestOcclusion(Origin, direction, visionRange, out RaycastHit2D hit))
                return hit.point;

            return Origin + direction * visionRange;
        }

        public void SetViewDirection(Vector2 direction)
        {
            viewDirection = NormalizeDirection(direction);
        }

        public bool RotateViewDirectionTowards(
            Vector2 targetDirection,
            float degreesPerSecond,
            float deltaTime)
        {
            Vector2 target = NormalizeDirection(targetDirection);
            float currentAngle = Mathf.Atan2(viewDirection.y, viewDirection.x) * Mathf.Rad2Deg;
            float targetAngle = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
            float nextAngle = Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                Mathf.Max(0f, degreesPerSecond) * Mathf.Max(0f, deltaTime));
            float nextRadians = nextAngle * Mathf.Deg2Rad;
            viewDirection = new Vector2(Mathf.Cos(nextRadians), Mathf.Sin(nextRadians));

            if (Vector2.Angle(viewDirection, target) > 0.05f)
                return false;

            viewDirection = target;
            return true;
        }

        public bool RotateViewDirectionTowards(Vector2 targetDirection, float deltaTime)
        {
            return RotateViewDirectionTowards(targetDirection, visionRotationSpeed, deltaTime);
        }

        private static Vector2 NormalizeDirection(Vector2 direction)
        {
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        }

        private Vector2 GetPlayerTargetPoint()
        {
            if (playerCollider != null && playerCollider.enabled)
                return playerCollider.bounds.center;

            return playerTransform.position;
        }

        private bool TryGetNearestOcclusion(
            Vector2 origin,
            Vector2 direction,
            float distance,
            out RaycastHit2D nearestHit)
        {
            nearestHit = default;
            if (visionOcclusionMask.value == 0)
                return false;

            int hitCount = Physics2D.RaycastNonAlloc(
                origin,
                direction,
                raycastHits,
                distance,
                visionOcclusionMask);

            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = raycastHits[i];
                Collider2D candidateCollider = candidate.collider;
                if (candidateCollider == null || candidateCollider.isTrigger)
                    continue;
                if (candidateCollider.transform == transform || candidateCollider.transform.IsChildOf(transform))
                    continue;
                if (playerTransform != null &&
                    (candidateCollider.transform == playerTransform || candidateCollider.transform.IsChildOf(playerTransform)))
                    continue;
                if (candidate.distance >= nearestDistance)
                    continue;

                nearestDistance = candidate.distance;
                nearestHit = candidate;
            }

            return nearestHit.collider != null;
        }

        private void ResolvePlayer()
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject == null)
            {
                playerTransform = null;
                playerCollider = null;
                return;
            }

            if ((playerMask.value & (1 << playerObject.layer)) == 0)
            {
                playerTransform = null;
                playerCollider = null;
                return;
            }

            playerTransform = playerObject.transform;
            Collider2D[] colliders = playerObject.GetComponentsInChildren<Collider2D>();
            playerCollider = null;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null || colliders[i].isTrigger || !colliders[i].enabled)
                    continue;

                playerCollider = colliders[i];
                break;
            }

            if (playerCollider == null)
                playerCollider = playerObject.GetComponentInChildren<Collider2D>();
        }

        private void AttemptResolvePlayer()
        {
            playerResolveTimer = 0.5f;
            ResolvePlayer();
        }

        private void OnValidate()
        {
            visionRange = Mathf.Max(0.1f, visionRange);
            visionAngle = Mathf.Clamp(visionAngle, 1f, 179f);
            detectionAcquireTime = Mathf.Max(0f, detectionAcquireTime);
            lostSightGrace = Mathf.Max(0f, lostSightGrace);
            visionRotationSpeed = Mathf.Max(1f, visionRotationSpeed);
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 origin = eyeOrigin != null ? eyeOrigin.position : transform.position;
            float halfAngle = Mathf.Clamp(visionAngle, 1f, 179f) * 0.5f;
            float range = Mathf.Max(0.1f, visionRange);
            Vector2 direction = Application.isPlaying ? viewDirection : Vector2.down;
            direction = NormalizeDirection(direction);
            Vector2 left = Quaternion.Euler(0f, 0f, -halfAngle) * direction;
            Vector2 right = Quaternion.Euler(0f, 0f, halfAngle) * direction;

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawLine(origin, origin + direction * range);
            Gizmos.DrawLine(origin, origin + left * range);
            Gizmos.DrawLine(origin, origin + right * range);
            Gizmos.DrawWireSphere(origin, 0.08f);
        }
    }
}
