using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Requested only after PlayerParry validates timing, position and facing.
    /// A future reflection unlock can add another action without changing detection.
    /// </summary>
    public enum ProjectileParryAction
    {
        Destroy
    }

    public enum ProjectileParryResolution
    {
        None,
        Destroyed
    }

    public readonly struct ProjectileParryContext
    {
        public ProjectileParryContext(
            Transform player,
            Vector2 parryOrigin,
            Vector2 parryDirection)
        {
            Player = player;
            ParryOrigin = parryOrigin;
            ParryDirection = parryDirection;
        }

        public Transform Player { get; }
        public Vector2 ParryOrigin { get; }
        public Vector2 ParryDirection { get; }
    }

    /// <summary>
    /// PlayerParry owns hit validation; the projectile owns the localized result.
    /// </summary>
    public interface IParryableProjectile
    {
        bool IsParryable { get; }
        Collider2D ParryCollider { get; }
        Vector2 Position { get; }

        ProjectileParryResolution ResolveParry(
            ProjectileParryAction action,
            ProjectileParryContext context);
    }
}
