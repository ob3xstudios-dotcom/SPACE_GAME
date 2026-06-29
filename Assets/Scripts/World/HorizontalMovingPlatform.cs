using UnityEngine;

namespace Game.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public class HorizontalMovingPlatform : MonoBehaviour
    {
        [Header("Path")]
        [SerializeField] private Vector2 localPointA = new Vector2(-2f, 0f);
        [SerializeField] private Vector2 localPointB = new Vector2(2f, 0f);
        [SerializeField, Min(0f)] private float speed = 2f;
        [SerializeField, Min(0f)] private float waitAtEnds = 0f;
        [SerializeField] private bool startMovingToB = true;

        private Rigidbody2D rb;
        private Vector2 origin;
        private bool movingToB;
        private float waitTimer;

        public Vector2 LastDelta { get; private set; }

        private Vector2 WorldPointA => origin + localPointA;
        private Vector2 WorldPointB => origin + localPointB;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            origin = rb.position;
            movingToB = startMovingToB;
        }

        private void FixedUpdate()
        {
            LastDelta = Vector2.zero;

            if (speed <= 0f) return;

            if (waitTimer > 0f)
            {
                waitTimer -= Time.fixedDeltaTime;
                return;
            }

            Vector2 target = movingToB ? WorldPointB : WorldPointA;
            Vector2 nextPosition = Vector2.MoveTowards(rb.position, target, speed * Time.fixedDeltaTime);
            LastDelta = nextPosition - rb.position;
            rb.MovePosition(nextPosition);

            if (Vector2.Distance(nextPosition, target) > 0.001f) return;

            movingToB = !movingToB;
            waitTimer = waitAtEnds;
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 basePosition = Application.isPlaying && rb != null ? origin : (Vector2)transform.position;
            Vector2 a = basePosition + localPointA;
            Vector2 b = basePosition + localPointB;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, 0.1f);
            Gizmos.DrawWireSphere(b, 0.1f);
        }
    }
}
