using UnityEngine;

namespace Game.Player
{
    public class PlayerCarryPoint : MonoBehaviour
    {
        [SerializeField] private PlayerController controller;
        [SerializeField] private Vector2 rightLocalPosition = new Vector2(0.75f, 0.85f);
        [SerializeField] private Vector2 leftLocalPosition = new Vector2(-0.75f, 0.85f);

        public Transform Point => transform;

        private void Awake()
        {
            ResolveRefs();
            Refresh();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void Reset()
        {
            ResolveRefs();
            Refresh();
        }

        private void OnValidate()
        {
            ResolveRefs();
            Refresh();
        }

        public void Refresh()
        {
            if (controller == null) return;

            Vector2 localPos = controller.FacingLeft ? leftLocalPosition : rightLocalPosition;
            if (transform.parent != null && transform.parent.lossyScale.x < 0f)
                localPos.x *= -1f;

            transform.localPosition = localPos;
        }

        private void ResolveRefs()
        {
            if (controller == null)
                controller = GetComponentInParent<PlayerController>();
        }
    }
}
