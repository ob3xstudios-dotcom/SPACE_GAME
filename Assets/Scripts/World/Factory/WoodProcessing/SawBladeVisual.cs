using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Replaceable visual-only blade rotation. Physics remains on the parent object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SawBladeVisual : MonoBehaviour
    {
        [SerializeField] private float rotationSpeed = 360f;

        private void Update()
        {
            transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}
