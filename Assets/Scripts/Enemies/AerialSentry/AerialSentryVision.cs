using UnityEngine;

namespace Game.Enemies.AerialSentry
{
    public enum AerialSentrySightFailure { None, NoPlayer, OutOfRange, OutsideAngle, Occluded }

    [RequireComponent(typeof(EnemyPerception2D))]
    public sealed class AerialSentryVision : MonoBehaviour
    {
        [SerializeField] private EnemyPerception2D perception;
        [SerializeField, Min(0f)] private float detectionAcquireTime = 0.15f;
        [SerializeField, Min(0f)] private float lostSightGrace = 0.15f;
        [SerializeField, Min(1f)] private float visionRotationSpeed = 360f;

        public Vector2 Origin => perception != null ? perception.Origin : transform.position;
        public float VisionRange => perception != null ? perception.VisionDistance : 0f;
        public float VisionAngle => perception != null ? perception.VisionAngle : 0f;
        public float DetectionAcquireTime => detectionAcquireTime;
        public float LostSightGrace => lostSightGrace;
        public float VisionRotationSpeed => visionRotationSpeed;
        public Transform PlayerTransform => perception != null ? perception.Target : null;
        public Vector2 ViewDirection => perception != null ? perception.FacingDirection : Vector2.down;
        public EnemyPerception2D Perception => perception;

        private void Awake()
        {
            if (perception == null)
                perception = GetComponent<EnemyPerception2D>();
            SetViewDirection(Vector2.down);
        }

        public bool TryGetVisibleTarget(out Vector2 targetPoint, out AerialSentrySightFailure failure)
        {
            targetPoint = perception != null ? perception.TargetPoint : default;
            if (perception == null || perception.Target == null)
            {
                failure = AerialSentrySightFailure.NoPlayer;
                return false;
            }
            if (!perception.IsPlayerVisible)
            {
                failure = AerialSentrySightFailure.OutsideAngle;
                return false;
            }
            failure = AerialSentrySightFailure.None;
            return true;
        }

        public bool IsPathOccluded(Vector2 targetPoint) =>
            perception != null && perception.IsPathOccluded(targetPoint);

        public Vector2 GetOccludedRayEnd(float signedAngle) =>
            perception != null ? perception.GetVisionRayEnd(signedAngle) : Origin;

        public void SetViewDirection(Vector2 direction) => perception?.SetFacingDirection(direction);

        public bool RotateViewDirectionTowards(Vector2 targetDirection, float degreesPerSecond, float deltaTime)
        {
            if (perception == null)
                return false;
            Vector2 target = NormalizeDirection(targetDirection);
            float currentAngle = Mathf.Atan2(ViewDirection.y, ViewDirection.x) * Mathf.Rad2Deg;
            float targetAngle = Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg;
            float nextAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle,
                Mathf.Max(0f, degreesPerSecond) * Mathf.Max(0f, deltaTime));
            Vector2 next = new Vector2(Mathf.Cos(nextAngle * Mathf.Deg2Rad),
                Mathf.Sin(nextAngle * Mathf.Deg2Rad));
            SetViewDirection(next);
            return Vector2.Angle(next, target) <= 0.05f;
        }

        public bool RotateViewDirectionTowards(Vector2 targetDirection, float deltaTime) =>
            RotateViewDirectionTowards(targetDirection, visionRotationSpeed, deltaTime);

        private static Vector2 NormalizeDirection(Vector2 direction) =>
            direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;

        private void OnValidate()
        {
            visionRotationSpeed = Mathf.Max(1f, visionRotationSpeed);
            detectionAcquireTime = Mathf.Max(0f, detectionAcquireTime);
            lostSightGrace = Mathf.Max(0f, lostSightGrace);
            if (perception == null)
                perception = GetComponent<EnemyPerception2D>();
        }
    }
}
