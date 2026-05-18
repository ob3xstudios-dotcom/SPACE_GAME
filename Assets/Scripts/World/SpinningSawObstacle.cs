using UnityEngine;
using Game.Combat;

namespace Game.World
{
    [RequireComponent(typeof(Collider2D))]
    public class SpinningSawObstacle : MonoBehaviour
    {
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField] private float rotationSpeed = 360f;

        private Collider2D damageCollider;

        private void Awake()
        {
            damageCollider = GetComponent<Collider2D>();
            damageCollider.isTrigger = true;
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
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
    }
}
