using UnityEngine;
using Game.Combat;

namespace Game.World
{
    [RequireComponent(typeof(Collider2D))]
    public class SpinningSawObstacle : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField, Min(1)] private int damage = 1;

        [Header("Spin")]
        [SerializeField] private float rotationSpeed = 360f;

        [Header("Path Movement")]
        [SerializeField] private bool enablePathMovement = false;
        [SerializeField] private Transform[] pathPoints;
        [SerializeField, Min(0f)] private float moveSpeed = 2f;
        [SerializeField] private bool loopPath = true;

        private Collider2D damageCollider;
        private int currentPathIndex;
        private int pathDirection = 1;

        private void Awake()
        {
            damageCollider = GetComponent<Collider2D>();
            damageCollider.isTrigger = true;
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
            MoveAlongPath();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null) return;

            IDamageable damageable =
                other.GetComponent<IDamageable>() ??
                other.GetComponentInParent<IDamageable>();

            if (damageable == null) return;

            damageable.TakeDamage(damage, transform.position);
        }

        private void MoveAlongPath()
        {
            if (!enablePathMovement || pathPoints == null || pathPoints.Length == 0 || moveSpeed <= 0f)
                return;

            Transform target = GetCurrentPathPoint();
            if (target == null) return;

            transform.position = Vector3.MoveTowards(transform.position, target.position, moveSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, target.position) <= 0.01f)
                AdvancePathIndex();
        }

        private Transform GetCurrentPathPoint()
        {
            currentPathIndex = Mathf.Clamp(currentPathIndex, 0, pathPoints.Length - 1);
            return pathPoints[currentPathIndex];
        }

        private void AdvancePathIndex()
        {
            if (pathPoints.Length <= 1) return;

            if (loopPath)
            {
                currentPathIndex = (currentPathIndex + 1) % pathPoints.Length;
                return;
            }

            if (currentPathIndex >= pathPoints.Length - 1)
                pathDirection = -1;
            else if (currentPathIndex <= 0)
                pathDirection = 1;

            currentPathIndex += pathDirection;
        }
    }
}
