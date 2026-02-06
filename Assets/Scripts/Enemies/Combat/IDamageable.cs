using UnityEngine;

namespace Game.Combat
{
    public interface IDamageable
    {
        void TakeDamage(int damage, Vector2 sourcePosition);
    }
}
