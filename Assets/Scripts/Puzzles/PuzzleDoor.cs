using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Kinematic solid receiver that moves between a captured closed position and an offset.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PuzzleDoor : PuzzleSignalReceiver
    {
        [Header("Door")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private BoxCollider2D solidCollider;
        [SerializeField] private bool openWhenActive = true;
        [SerializeField] private Vector2 openOffset = new Vector2(0f, 3f);
        [SerializeField, Min(0f)] private float openSpeed = 3f;
        [SerializeField, Min(0f)] private float closeSpeed = 3f;
        [SerializeField] private bool startOpen;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        private Vector2 closedPosition;
        private Vector2 openPosition;
        private bool targetOpen;
        private bool hasReceivedSignal;
        private bool initialized;

        public bool IsTargetOpen => targetOpen;

        private void Awake()
        {
            Initialize();
        }

        private void FixedUpdate()
        {
            Initialize();
            if (body == null)
                return;

            Vector2 target = targetOpen ? openPosition : closedPosition;
            if ((body.position - target).sqrMagnitude <= 0.000001f)
                return;

            float speed = targetOpen ? openSpeed : closeSpeed;
            if (speed <= 0f)
                return;

            Vector2 nextPosition = Vector2.MoveTowards(
                body.position,
                target,
                speed * Time.fixedDeltaTime);
            body.MovePosition(nextPosition);
        }

        public override void ApplyPuzzleSignal(bool active)
        {
            bool requestedOpen = openWhenActive ? active : !active;
            bool targetChanged = !hasReceivedSignal || targetOpen != requestedOpen;

            hasReceivedSignal = true;
            targetOpen = requestedOpen;

            if (targetChanged && debugLogs)
                Debug.Log($"[PUZZLE][DOOR] name={name} target={(targetOpen ? "Open" : "Closed")}", this);
        }

        private void Initialize()
        {
            if (initialized)
                return;

            ResolveReferences();
            ApplyPhysicsConfiguration();
            closedPosition = body != null ? body.position : (Vector2)transform.position;
            openPosition = closedPosition + openOffset;

            if (!hasReceivedSignal)
                targetOpen = startOpen;

            initialized = true;
        }

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();

            if (solidCollider == null)
                solidCollider = GetComponent<BoxCollider2D>();
        }

        private void ApplyPhysicsConfiguration()
        {
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = true;
                body.gravityScale = 0f;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            }

            if (solidCollider != null)
                solidCollider.isTrigger = false;
        }

        private void Reset()
        {
            ResolveReferences();
            ApplyPhysicsConfiguration();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            openSpeed = Mathf.Max(0f, openSpeed);
            closeSpeed = Mathf.Max(0f, closeSpeed);
            ResolveReferences();
            ApplyPhysicsConfiguration();
        }
#endif

        private void OnDrawGizmosSelected()
        {
            ResolveReferences();

            Vector2 closed = Application.isPlaying && initialized
                ? closedPosition
                : (Vector2)transform.position;
            Vector2 open = closed + openOffset;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;

            DrawDoorVolume(closed, new Color(1f, 0.45f, 0.2f, 0.9f));
            DrawDoorVolume(open, new Color(0.2f, 1f, 0.45f, 0.9f));

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawLine(closed, open);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private void DrawDoorVolume(Vector2 rootPosition, Color color)
        {
            Vector2 offset = solidCollider != null ? solidCollider.offset : Vector2.zero;
            Vector2 size = solidCollider != null ? solidCollider.size : Vector2.one;
            Gizmos.matrix = Matrix4x4.TRS(
                new Vector3(rootPosition.x, rootPosition.y, transform.position.z),
                transform.rotation,
                transform.lossyScale);
            Gizmos.color = color;
            Gizmos.DrawWireCube(offset, size);
        }
    }
}
