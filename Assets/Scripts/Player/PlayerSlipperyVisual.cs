using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Runtime-only green droplets for slippery feedback. It never modifies the Player's
    /// existing SpriteRenderers and has no gameplay authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSlipperyVisual : MonoBehaviour
    {
        private const int DropCount = 5;

        private readonly Color dropColor = new Color(0.08f, 1f, 0.24f, 0.9f);
        private Transform[] dropTransforms;
        private LineRenderer[] drops;
        private Material runtimeMaterial;
        private bool visible;

        public void SetVisible(bool shouldBeVisible)
        {
            visible = shouldBeVisible;
            if (visible)
                EnsureVisual();

            if (drops == null)
                return;

            for (int i = 0; i < drops.Length; i++)
            {
                if (drops[i] != null)
                    drops[i].enabled = visible;
            }
        }

        private void Update()
        {
            if (!visible || dropTransforms == null)
                return;

            float time = Time.time;
            for (int i = 0; i < dropTransforms.Length; i++)
            {
                Transform drop = dropTransforms[i];
                if (drop == null)
                    continue;

                float phase = time * 2.6f + i * 1.37f;
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * (0.42f + 0.12f * Mathf.Sin(phase * 0.73f));
                float y = -0.45f + i * 0.28f + 0.09f * Mathf.Sin(phase);
                float pulse = 0.8f + 0.2f * Mathf.Sin(phase * 1.4f);
                drop.localPosition = new Vector3(x, y, 0f);
                drop.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void EnsureVisual()
        {
            if (drops != null)
                return;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                runtimeMaterial = new Material(shader)
                {
                    name = "Player Slippery Drops (Runtime)"
                };
            }

            dropTransforms = new Transform[DropCount];
            drops = new LineRenderer[DropCount];

            for (int i = 0; i < DropCount; i++)
            {
                GameObject dropObject = new GameObject($"SlipperyDrop_{i + 1}");
                dropObject.hideFlags = HideFlags.DontSave;
                dropObject.layer = gameObject.layer;
                dropObject.transform.SetParent(transform, false);

                LineRenderer line = dropObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 3;
                line.SetPosition(0, new Vector3(0f, 0.12f, 0f));
                line.SetPosition(1, new Vector3(0.025f, 0.02f, 0f));
                line.SetPosition(2, new Vector3(0f, -0.1f, 0f));
                line.startWidth = 0.11f;
                line.endWidth = 0.025f;
                line.startColor = dropColor;
                line.endColor = new Color(dropColor.r, dropColor.g, dropColor.b, 0.35f);
                line.numCapVertices = 4;
                line.numCornerVertices = 3;
                line.sortingLayerName = "Characters";
                line.sortingOrder = 25;
                if (runtimeMaterial != null)
                    line.sharedMaterial = runtimeMaterial;

                dropTransforms[i] = dropObject.transform;
                drops[i] = line;
            }
        }
    }
}
