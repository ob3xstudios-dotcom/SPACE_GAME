using System;
using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    public enum WoodWorkpieceState
    {
        VerticalLog,
        VerticalBoards,
        Turning,
        HorizontalBoards,
        CrossCut1,
        CrossCut2,
        CrossCut3,
        Disposed
    }

    /// <summary>
    /// Owns the canonical state of one physical piece throughout the whole line.
    /// Visual changes are delegated to an optional child component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class WoodWorkpiece : MonoBehaviour
    {
        [Header("Physics")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D solidCollider;

        [Header("State")]
        [SerializeField] private WoodWorkpieceState initialState = WoodWorkpieceState.VerticalLog;
        [SerializeField] private WoodWorkpieceState currentState;
        [SerializeField] private WoodWorkpieceVisual visual;

        private ConveyorRider2D conveyorRider;
        private RigidbodyType2D initialBodyType;
        private RigidbodyConstraints2D initialConstraints;
        private RigidbodyInterpolation2D initialInterpolation;
        private CollisionDetectionMode2D initialCollisionDetection;
        private float initialGravityScale;
        private bool initialSimulated;
        private bool initialColliderEnabled;
        private bool initialPhysicsCaptured;
        private bool releaseNotified;

        public event Action<WoodWorkpieceState> StateChanged;
        public event Action<WoodWorkpiece> Released;

        public Rigidbody2D Body => body;
        public Collider2D SolidCollider => solidCollider;
        public WoodWorkpieceState State => currentState;
        public bool IsDisposed => State == WoodWorkpieceState.Disposed;
        public WoodLogSpawner OwnerSpawner { get; private set; }

        public int CrossCutIndex
        {
            get
            {
                switch (State)
                {
                    case WoodWorkpieceState.CrossCut1:
                        return 1;
                    case WoodWorkpieceState.CrossCut2:
                        return 2;
                    case WoodWorkpieceState.CrossCut3:
                        return 3;
                    default:
                        return 0;
                }
            }
        }

        private void Awake()
        {
            ResolveReferences();
            CaptureInitialPhysicsState();
            currentState = initialState == WoodWorkpieceState.Disposed
                ? WoodWorkpieceState.VerticalLog
                : initialState;
            visual?.SetState(State);
        }

        private void OnValidate()
        {
            ResolveReferences();

            if (!Application.isPlaying && visual != null)
                visual.SetState(initialState);
        }

        private void OnDestroy()
        {
            NotifyReleased();
        }

        public bool TryApplyLongitudinalCut()
        {
            return TryTransition(WoodWorkpieceState.VerticalLog, WoodWorkpieceState.VerticalBoards);
        }

        public bool TryBeginTurning()
        {
            return TryTransition(WoodWorkpieceState.VerticalBoards, WoodWorkpieceState.Turning);
        }

        public bool TryFinishTurning()
        {
            return TryTransition(WoodWorkpieceState.Turning, WoodWorkpieceState.HorizontalBoards);
        }

        public bool TryCancelTurning()
        {
            return TryTransition(WoodWorkpieceState.Turning, WoodWorkpieceState.VerticalBoards);
        }

        public bool TryApplyCrossCut(int cutIndex)
        {
            switch (cutIndex)
            {
                case 1:
                    return TryTransition(WoodWorkpieceState.HorizontalBoards, WoodWorkpieceState.CrossCut1);
                case 2:
                    return TryTransition(WoodWorkpieceState.CrossCut1, WoodWorkpieceState.CrossCut2);
                case 3:
                    return TryTransition(WoodWorkpieceState.CrossCut2, WoodWorkpieceState.CrossCut3);
                default:
                    return false;
            }
        }

        public bool TryDispose()
        {
            if (State == WoodWorkpieceState.Disposed)
                return false;

            SetState(WoodWorkpieceState.Disposed);
            NotifyReleased();
            Destroy(gameObject);
            return true;
        }

        /// <summary>
        /// Restores this same instance to the canonical beginning of the line.
        /// The owner reference and Released subscription deliberately survive.
        /// </summary>
        public void ResetForRespawn(Vector3 spawnPosition, Quaternion spawnRotation, Vector2 initialVelocity)
        {
            ResolveReferences();
            CaptureInitialPhysicsState();

            conveyorRider?.ResetForTeleport();
            releaseNotified = false;

            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }

            if (solidCollider != null)
                solidCollider.enabled = false;

            transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            if (body != null)
            {
                body.bodyType = initialBodyType;
                body.constraints = initialConstraints;
                body.gravityScale = initialGravityScale;
                body.interpolation = initialInterpolation;
                body.collisionDetectionMode = initialCollisionDetection;
                body.position = spawnPosition;
                body.rotation = spawnRotation.eulerAngles.z;
            }

            SetState(WoodWorkpieceState.VerticalLog);

            if (solidCollider != null)
                solidCollider.enabled = initialColliderEnabled;

            if (body != null)
            {
                body.simulated = initialSimulated;
                body.angularVelocity = 0f;
                body.velocity = initialBodyType == RigidbodyType2D.Dynamic && initialSimulated
                    ? initialVelocity
                    : Vector2.zero;
            }
        }

        internal void AssignOwner(WoodLogSpawner ownerSpawner)
        {
            OwnerSpawner = ownerSpawner;
        }

        internal void PrepareForDeferredRecycle()
        {
            conveyorRider?.ResetForTeleport();

            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }

            if (solidCollider != null)
                solidCollider.enabled = false;
        }

        private bool TryTransition(WoodWorkpieceState expected, WoodWorkpieceState next)
        {
            if (State != expected)
                return false;

            SetState(next);
            return true;
        }

        private void SetState(WoodWorkpieceState next)
        {
            currentState = next;
            visual?.SetState(next);
            StateChanged?.Invoke(next);
        }

        private void NotifyReleased()
        {
            if (releaseNotified)
                return;

            releaseNotified = true;
            Released?.Invoke(this);
        }

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();

            if (solidCollider == null)
                solidCollider = GetComponent<Collider2D>();

            if (visual == null)
                visual = GetComponentInChildren<WoodWorkpieceVisual>(true);

            if (conveyorRider == null)
                conveyorRider = GetComponent<ConveyorRider2D>();
        }

        private void CaptureInitialPhysicsState()
        {
            if (initialPhysicsCaptured || body == null || solidCollider == null)
                return;

            initialBodyType = body.bodyType;
            initialConstraints = body.constraints;
            initialGravityScale = body.gravityScale;
            initialInterpolation = body.interpolation;
            initialCollisionDetection = body.collisionDetectionMode;
            initialSimulated = body.simulated;
            initialColliderEnabled = solidCollider.enabled;
            initialPhysicsCaptured = true;
        }
    }
}
