using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Recycles owned WoodWorkpiece instances through their spawner. Unowned pieces
    /// retain the safe legacy disposal fallback.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class WoodDisposalTrigger : MonoBehaviour
    {
        [SerializeField] private bool recycleToSpawner = true;

        private BoxCollider2D disposalTrigger;

        private void Awake()
        {
            disposalTrigger = GetComponent<BoxCollider2D>();
            disposalTrigger.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            WoodWorkpiece workpiece = other.GetComponentInParent<WoodWorkpiece>();
            if (workpiece == null)
                return;

            if (recycleToSpawner && workpiece.OwnerSpawner != null &&
                workpiece.OwnerSpawner.TryRecycle(workpiece))
            {
                return;
            }

            workpiece.TryDispose();
        }

        private void OnDrawGizmosSelected()
        {
            BoxCollider2D box = disposalTrigger != null ? disposalTrigger : GetComponent<BoxCollider2D>();
            if (box == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.72f, 0.16f, 0.95f, 0.95f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
