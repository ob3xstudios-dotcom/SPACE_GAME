using System.Collections.Generic;
using UnityEngine;
using Game.Player;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Kinematic saw that cycles top-to-bottom-to-top. Player damage is active on
    /// every phase; ordered wood cuts are accepted only while travelling down.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class ReciprocatingSawTrap : MonoBehaviour
    {
        private enum MotionPhase
        {
            InitialDelay,
            WaitingAtTop,
            MovingDown,
            WaitingAtBottom,
            MovingUp
        }

        [Header("Path")]
        [SerializeField] private Transform upperPoint;
        [SerializeField] private Transform lowerPoint;
        [SerializeField, Min(0f)] private float downSpeed = 3f;
        [SerializeField, Min(0f)] private float upSpeed = 2.5f;
        [SerializeField, Min(0f)] private float waitAtTop = 0.25f;
        [SerializeField, Min(0f)] private float waitAtBottom = 0.12f;
        [SerializeField, Min(0f)] private float initialDelay;

        [Header("Blade")]
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Range(1, 3)] private int cutIndex = 1;

        private readonly Dictionary<Collider2D, WoodWorkpiece> woodContacts =
            new Dictionary<Collider2D, WoodWorkpiece>();
        private readonly Dictionary<Collider2D, PlayerHealth> playerContacts =
            new Dictionary<Collider2D, PlayerHealth>();
        private readonly HashSet<WoodWorkpiece> cutThisDescent =
            new HashSet<WoodWorkpiece>();
        private readonly HashSet<PlayerHealth> damagedThisPhysicsStep =
            new HashSet<PlayerHealth>();

        private Rigidbody2D body;
        private Collider2D bladeCollider;
        private MotionPhase phase;
        private float phaseTimer;
        private Vector2 fallbackUpperPoint;
        private Vector2 fallbackLowerPoint;

        public int CutIndex => cutIndex;
        public bool IsMovingDown => phase == MotionPhase.MovingDown;

        private Vector2 UpperPosition => upperPoint != null ? upperPoint.position : fallbackUpperPoint;
        private Vector2 LowerPosition => lowerPoint != null ? lowerPoint.position : fallbackLowerPoint;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bladeCollider = GetComponent<Collider2D>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            bladeCollider.isTrigger = true;

            fallbackUpperPoint = body.position;
            fallbackLowerPoint = body.position + Vector2.down * 2f;
        }

        private void Start()
        {
            body.position = UpperPosition;

            if (initialDelay > 0f)
            {
                phase = MotionPhase.InitialDelay;
                phaseTimer = initialDelay;
            }
            else if (waitAtTop > 0f)
            {
                phase = MotionPhase.WaitingAtTop;
                phaseTimer = waitAtTop;
            }
            else
            {
                BeginMovingDown();
            }
        }

        private void FixedUpdate()
        {
            damagedThisPhysicsStep.Clear();

            switch (phase)
            {
                case MotionPhase.InitialDelay:
                    TickDelayAndBeginDown();
                    break;
                case MotionPhase.WaitingAtTop:
                    TickDelayAndBeginDown();
                    break;
                case MotionPhase.MovingDown:
                    MoveTowards(LowerPosition, downSpeed, MotionPhase.WaitingAtBottom, waitAtBottom);
                    break;
                case MotionPhase.WaitingAtBottom:
                    TickDelayAndBeginUp();
                    break;
                case MotionPhase.MovingUp:
                    MoveTowards(UpperPosition, upSpeed, MotionPhase.WaitingAtTop, waitAtTop);
                    break;
            }
        }

        private void OnValidate()
        {
            downSpeed = Mathf.Max(0f, downSpeed);
            upSpeed = Mathf.Max(0f, upSpeed);
            waitAtTop = Mathf.Max(0f, waitAtTop);
            waitAtBottom = Mathf.Max(0f, waitAtBottom);
            initialDelay = Mathf.Max(0f, initialDelay);
            cutIndex = Mathf.Clamp(cutIndex, 1, 3);
            damage = Mathf.Max(1, damage);
        }

        private void OnDisable()
        {
            woodContacts.Clear();
            playerContacts.Clear();
            cutThisDescent.Clear();
            damagedThisPhysicsStep.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterAndProcess(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterAndProcess(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            woodContacts.Remove(other);
            playerContacts.Remove(other);
        }

        private void RegisterAndProcess(Collider2D other)
        {
            if (other == null)
                return;

            if (!playerContacts.TryGetValue(other, out PlayerHealth playerHealth))
            {
                playerHealth = other.GetComponentInParent<PlayerHealth>();
                playerContacts[other] = playerHealth;
            }

            if (playerHealth != null && damagedThisPhysicsStep.Add(playerHealth))
                playerHealth.TakeDamage(damage, transform.position);

            if (!IsMovingDown)
                return;

            if (!woodContacts.TryGetValue(other, out WoodWorkpiece workpiece))
            {
                workpiece = other.GetComponentInParent<WoodWorkpiece>();
                woodContacts[other] = workpiece;
            }

            if (workpiece != null && !cutThisDescent.Contains(workpiece) &&
                workpiece.TryApplyCrossCut(cutIndex))
            {
                cutThisDescent.Add(workpiece);
            }
        }

        private void TickDelayAndBeginDown()
        {
            phaseTimer -= Time.fixedDeltaTime;
            if (phaseTimer <= 0f)
                BeginMovingDown();
        }

        private void TickDelayAndBeginUp()
        {
            phaseTimer -= Time.fixedDeltaTime;
            if (phaseTimer <= 0f)
                phase = MotionPhase.MovingUp;
        }

        private void BeginMovingDown()
        {
            cutThisDescent.Clear();
            phase = MotionPhase.MovingDown;
        }

        private void MoveTowards(Vector2 target, float speed, MotionPhase arrivalPhase, float waitDuration)
        {
            if (speed <= 0f)
                return;

            Vector2 nextPosition = Vector2.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);
            body.MovePosition(nextPosition);

            if ((nextPosition - target).sqrMagnitude > 0.000001f)
                return;

            phase = arrivalPhase;
            phaseTimer = waitDuration;

            if (phase == MotionPhase.WaitingAtTop && phaseTimer <= 0f)
                BeginMovingDown();
            else if (phase == MotionPhase.WaitingAtBottom && phaseTimer <= 0f)
                phase = MotionPhase.MovingUp;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 upper = upperPoint != null ? upperPoint.position : transform.position;
            Vector3 lower = lowerPoint != null ? lowerPoint.position : transform.position + Vector3.down * 2f;

            Gizmos.color = new Color(1f, 0.25f, 0.16f, 0.95f);
            Gizmos.DrawLine(upper, lower);
            Gizmos.DrawWireSphere(upper, 0.13f);
            Gizmos.DrawWireSphere(lower, 0.13f);

            int visibleIndex = Mathf.Clamp(cutIndex, 1, 3);
            for (int i = 0; i < visibleIndex; i++)
            {
                Vector3 marker = upper + new Vector3((i - (visibleIndex - 1) * 0.5f) * 0.16f, 0.26f, 0f);
                Gizmos.DrawWireCube(marker, Vector3.one * 0.08f);
            }
        }
    }
}
