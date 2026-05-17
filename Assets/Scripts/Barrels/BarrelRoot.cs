using UnityEngine;

namespace Game.Barrels
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class BarrelRoot : MonoBehaviour
    {
        [SerializeField] private string barrelId;
        [SerializeField] private Rigidbody2D rb;

        public string BarrelId => barrelId;
        public Rigidbody2D Rigidbody => rb;

        private void Awake()
        {
            ResolveRefs();
        }

        private void Reset()
        {
            ResolveRefs();

            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.freezeRotation = true;
            }

            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = false;
        }

        private void OnValidate()
        {
            ResolveRefs();
        }

        private void ResolveRefs()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody2D>();
        }
    }
}
