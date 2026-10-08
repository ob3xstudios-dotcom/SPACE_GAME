using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Captures one VerticalBoards workpiece, drives a deterministic kinematic turn,
    /// then restores its previous physics configuration at the output point.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class WoodTurnerMachine : MonoBehaviour
    {
        [Header("Path")]
        [SerializeField] private Transform inputPoint;
        [SerializeField] private Transform turnPivot;
        [SerializeField] private Transform outputPoint;
        [SerializeField, Min(0f)] private float turnDuration = 1f;
        [SerializeField] private float targetRotation = -90f;

        private readonly Dictionary<Collider2D, WoodWorkpiece> contacts =
            new Dictionary<Collider2D, WoodWorkpiece>();

        private Collider2D captureTrigger;
        private WoodWorkpiece capturedPiece;
        private Rigidbody2D capturedBody;
        private ConveyorRider2D capturedRider;
        private Coroutine turnRoutine;
        private WaitForFixedUpdate waitForFixedUpdate;

        private RigidbodyType2D previousBodyType;
        private RigidbodyConstraints2D previousConstraints;
        private RigidbodyInterpolation2D previousInterpolation;
        private CollisionDetectionMode2D previousCollisionDetection;
        private float previousGravityScale;
        private bool previousRiderEnabled;

        public bool IsOccupied => capturedPiece != null;

        private void Awake()
        {
            captureTrigger = GetComponent<Collider2D>();
            captureTrigger.isTrigger = true;
            waitForFixedUpdate = new WaitForFixedUpdate();
        }

        private void OnDisable()
        {
            contacts.Clear();
            CancelCapturedPiece();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterAndTryCapture(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterAndTryCapture(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            contacts.Remove(other);
        }

        private void RegisterAndTryCapture(Collider2D other)
        {
            if (capturedPiece != null || other == null)
                return;

            if (!contacts.TryGetValue(other, out WoodWorkpiece workpiece))
            {
                workpiece = other.GetComponentInParent<WoodWorkpiece>();
                contacts[other] = workpiece;
            }

            if (workpiece == null || !workpiece.TryBeginTurning())
                return;

            Capture(workpiece);
        }

        private void Capture(WoodWorkpiece workpiece)
        {
            Rigidbody2D body = workpiece.Body;
            if (body == null)
            {
                workpiece.TryCancelTurning();
                return;
            }

            capturedPiece = workpiece;
            capturedBody = body;
            capturedRider = workpiece.GetComponent<ConveyorRider2D>();

            previousBodyType = body.bodyType;
            previousConstraints = body.constraints;
            previousInterpolation = body.interpolation;
            previousCollisionDetection = body.collisionDetectionMode;
            previousGravityScale = body.gravityScale;
            previousRiderEnabled = capturedRider != null && capturedRider.TransportEnabled;

            if (capturedRider != null)
                capturedRider.SetTransportEnabled(false);

            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.None;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            turnRoutine = StartCoroutine(TurnRoutine());
        }

        private IEnumerator TurnRoutine()
        {
            Vector2 startPosition = capturedBody.position;
            float startRotation = capturedBody.rotation;
            Vector2 inputPosition = inputPoint != null ? inputPoint.position : startPosition;
            Vector2 controlPosition = turnPivot != null ? turnPivot.position : inputPosition;
            Vector2 outputPosition = outputPoint != null ? outputPoint.position : controlPosition;

            if (turnDuration > 0f)
            {
                float captureDuration = turnDuration * 0.25f;
                float turnPhaseDuration = Mathf.Max(0.0001f, turnDuration - captureDuration);
                float elapsed = 0f;

                while (elapsed < captureDuration && CapturedBodyIsValid())
                {
                    elapsed += Time.fixedDeltaTime;
                    float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, captureDuration));
                    float eased = SmoothStep(normalized);
                    capturedBody.MovePosition(Vector2.Lerp(startPosition, inputPosition, eased));
                    yield return waitForFixedUpdate;
                }

                elapsed = 0f;
                while (elapsed < turnPhaseDuration && CapturedBodyIsValid())
                {
                    elapsed += Time.fixedDeltaTime;
                    float normalized = Mathf.Clamp01(elapsed / turnPhaseDuration);
                    float eased = SmoothStep(normalized);
                    capturedBody.MovePosition(QuadraticBezier(inputPosition, controlPosition, outputPosition, eased));
                    capturedBody.MoveRotation(Mathf.LerpAngle(startRotation, startRotation + targetRotation, eased));
                    yield return waitForFixedUpdate;
                }
            }

            if (!CapturedBodyIsValid())
            {
                ClearCaptureReferences();
                yield break;
            }

            capturedBody.position = outputPosition;
            capturedBody.rotation = startRotation + targetRotation;
            capturedPiece.TryFinishTurning();
            RestoreCapturedPhysics();
            ClearCaptureReferences();
        }

        private void CancelCapturedPiece()
        {
            if (turnRoutine != null)
            {
                StopCoroutine(turnRoutine);
                turnRoutine = null;
            }

            if (capturedPiece != null)
                capturedPiece.TryCancelTurning();

            if (capturedBody != null)
                RestoreCapturedPhysics();

            ClearCaptureReferences();
        }

        private void RestoreCapturedPhysics()
        {
            capturedBody.bodyType = previousBodyType;
            capturedBody.gravityScale = previousGravityScale;
            capturedBody.constraints = previousConstraints;
            capturedBody.interpolation = previousInterpolation;
            capturedBody.collisionDetectionMode = previousCollisionDetection;
            capturedBody.velocity = Vector2.zero;
            capturedBody.angularVelocity = 0f;

            if (capturedRider != null)
                capturedRider.SetTransportEnabled(previousRiderEnabled);
        }

        private bool CapturedBodyIsValid()
        {
            return capturedPiece != null && capturedBody != null;
        }

        private void ClearCaptureReferences()
        {
            capturedPiece = null;
            capturedBody = null;
            capturedRider = null;
            turnRoutine = null;
        }

        private static Vector2 QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 input = inputPoint != null ? inputPoint.position : transform.position;
            Vector3 pivot = turnPivot != null ? turnPivot.position : input;
            Vector3 output = outputPoint != null ? outputPoint.position : pivot;

            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.95f);
            Gizmos.DrawWireSphere(input, 0.13f);
            Gizmos.DrawLine(input, pivot);

            Gizmos.color = new Color(1f, 0.72f, 0.15f, 0.95f);
            Gizmos.DrawWireSphere(pivot, 0.16f);
            Gizmos.DrawLine(pivot, output);

            Gizmos.color = new Color(0.35f, 1f, 0.4f, 0.95f);
            Gizmos.DrawWireSphere(output, 0.13f);
        }
    }
}
