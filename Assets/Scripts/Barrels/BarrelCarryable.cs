using Game.Interaction;
using Game.Player;
using System;
using UnityEngine;

namespace Game.Barrels
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class BarrelCarryable : MonoBehaviour, IInteractable
    {
        [Header("Carry")]
        [SerializeField] private bool canCarry = true;
        [SerializeField] private bool disableSolidCollidersWhileCarried = true;
        [SerializeField, Min(0f)] private float dropDistance = 0.75f;
        [SerializeField] private Vector2 dropOffset = new Vector2(0f, 0.1f);
        [SerializeField, Min(0f)] private float dropForwardImpulse = 0f;
        [SerializeField, Min(0f)] private float dropUpImpulse = 0f;

        [Header("Refs")]
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private Collider2D[] solidColliders;

        private Transform carrier;
        private PlayerCarryPoint carryPoint;
        private PlayerController carrierController;
        private Transform originalParent;
        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravityScale;
        private bool isCarried;

        public Transform InteractableTransform => transform;
        public bool IsCarried => isCarried;
        public event Action<bool> CarryStateChanged;

        private void Awake()
        {
            ResolveRefs();
            CacheRigidbodyState();
        }

        private void Reset()
        {
            ResolveRefs();
        }

        private void OnValidate()
        {
            ResolveRefs();
        }

        private void OnDisable()
        {
            if (isCarried)
                Drop();
        }

        private void LateUpdate()
        {
            if (!isCarried || carryPoint == null) return;

            carryPoint.Refresh();
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        public bool CanInteract(GameObject interactor)
        {
            if (!canCarry || !isActiveAndEnabled) return false;
            if (isCarried) return interactor != null && interactor.transform == carrier;

            PlayerController controller = interactor != null ? interactor.GetComponentInParent<PlayerController>() : null;
            return controller != null && !controller.IsCarrying;
        }

        public void Interact(GameObject interactor)
        {
            if (isCarried) return;
            TryPickUp(interactor);
        }

        public bool TryPickUp(GameObject interactor)
        {
            PlayerController controller = interactor != null ? interactor.GetComponentInParent<PlayerController>() : null;
            return TryPickUp(controller);
        }

        public bool TryPickUp(PlayerController controller)
        {
            if (!canCarry || !isActiveAndEnabled || isCarried) return false;
            if (controller == null || controller.IsCarrying) return false;

            carryPoint = controller.GetComponentInChildren<PlayerCarryPoint>(true);
            if (carryPoint == null) return false;

            if (!controller.TrySetCarriedObject(this))
                return false;

            carrierController = controller;
            carrier = carrierController.transform;
            originalParent = transform.parent;
            CacheRigidbodyState();

            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = 0f;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;

            carryPoint.Refresh();
            transform.SetParent(carryPoint.Point, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            SetSolidCollidersEnabled(!disableSolidCollidersWhileCarried);
            isCarried = true;
            CarryStateChanged?.Invoke(true);
            return true;
        }

        public void Drop()
        {
            if (!isCarried) return;

            int facing = carrierController != null && carrierController.FacingLeft ? -1 : 1;
            Vector3 dropPosition = GetDropPosition(facing);

            isCarried = false;
            transform.SetParent(originalParent, true);
            transform.position = dropPosition;
            transform.rotation = Quaternion.identity;

            carrierController?.ClearCarriedObject(this);

            carrier = null;
            carryPoint = null;
            carrierController = null;
            originalParent = null;

            rb.bodyType = cachedBodyType;
            rb.constraints = cachedConstraints;
            rb.gravityScale = cachedGravityScale;
            rb.velocity = new Vector2(facing * dropForwardImpulse, dropUpImpulse);
            rb.angularVelocity = 0f;

            SetSolidCollidersEnabled(true);
            CarryStateChanged?.Invoke(false);
        }

        private Vector3 GetDropPosition(int facing)
        {
            Vector3 origin = carrier != null ? carrier.position : transform.position;
            return origin + new Vector3(facing * dropDistance + dropOffset.x * facing, dropOffset.y, 0f);
        }

        private void CacheRigidbodyState()
        {
            if (rb == null) return;

            cachedBodyType = rb.bodyType;
            cachedConstraints = rb.constraints;
            cachedGravityScale = rb.gravityScale;
        }

        private void SetSolidCollidersEnabled(bool enabled)
        {
            if (solidColliders == null) return;

            for (int i = 0; i < solidColliders.Length; i++)
            {
                Collider2D col = solidColliders[i];
                if (col != null && !col.isTrigger)
                    col.enabled = enabled;
            }
        }

        private void ResolveRefs()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody2D>();

            if (solidColliders == null || solidColliders.Length == 0)
                solidColliders = GetComponentsInChildren<Collider2D>(true);
        }
    }
}
