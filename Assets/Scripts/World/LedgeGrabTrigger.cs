using UnityEngine;

namespace Game.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    [AddComponentMenu("Game/World/Ledge Grab Trigger")]
    public class LedgeGrabTrigger : LedgeGrabPoint
    {
    }
}
