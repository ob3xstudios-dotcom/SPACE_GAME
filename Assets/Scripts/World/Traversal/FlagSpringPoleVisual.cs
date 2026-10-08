using UnityEngine;

namespace Game.World.Traversal
{
    /// <summary>
    /// Replaceable procedural placeholder. It contributes renderers only; traversal
    /// and collision never depend on this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlagSpringPoleVisual : MonoBehaviour
    {
        [SerializeField] private Transform polePivot;
        [SerializeField] private Transform poleVisual;
        [SerializeField] private Transform flagVisual;
        [SerializeField] private Transform grabPoint;
        [SerializeField] private Transform baseVisual;
        [SerializeField] private Color poleColor = new Color(0.82f, 0.86f, 0.9f, 1f);
        [SerializeField] private Color flagColor = new Color(1f, 0.28f, 0.22f, 1f);
        [SerializeField] private Color baseColor = new Color(0.28f, 0.32f, 0.38f, 1f);
        [SerializeField, Min(0.02f)] private float poleWidth = 0.12f;
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 5;

        private LineRenderer poleLine;
        private LineRenderer flagLine;
        private LineRenderer baseLine;
        private Material runtimeMaterial;

        private void OnEnable()
        {
            ResolveReferences();
            EnsureVisual();
            SetEnabled(true);
        }

        private void Update()
        {
            EnsureVisual();
            UpdateGeometry();
        }

        private void OnDisable()
        {
            SetEnabled(false);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private void OnValidate()
        {
            poleWidth = Mathf.Max(0.02f, poleWidth);
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (polePivot == null)
                polePivot = transform.Find("PolePivot");
            if (polePivot != null)
            {
                if (poleVisual == null)
                    poleVisual = polePivot.Find("PoleVisual");
                if (flagVisual == null)
                    flagVisual = polePivot.Find("FlagVisual");
                if (grabPoint == null)
                    grabPoint = polePivot.Find("GrabPoint");
            }
            if (baseVisual == null)
                baseVisual = transform.Find("BaseVisual");
        }

        private void EnsureVisual()
        {
            ResolveReferences();
            EnsureMaterial();
            poleLine = EnsureLine(poleVisual, poleLine, poleColor, poleWidth, sortingOrder);
            flagLine = EnsureLine(
                flagVisual,
                flagLine,
                flagColor,
                Mathf.Max(0.025f, poleWidth * 0.55f),
                sortingOrder + 1);
            baseLine = EnsureLine(
                baseVisual,
                baseLine,
                baseColor,
                Mathf.Max(0.08f, poleWidth * 1.4f),
                sortingOrder);
        }

        private void EnsureMaterial()
        {
            if (runtimeMaterial != null)
                return;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
                return;

            runtimeMaterial = new Material(shader)
            {
                name = "Flag Spring Pole Visual (Runtime)"
            };
        }

        private LineRenderer EnsureLine(
            Transform owner,
            LineRenderer current,
            Color color,
            float width,
            int order)
        {
            if (owner == null)
                return null;
            if (current == null)
                current = owner.GetComponent<LineRenderer>();
            if (current == null)
                current = owner.gameObject.AddComponent<LineRenderer>();

            current.useWorldSpace = false;
            current.alignment = LineAlignment.View;
            current.textureMode = LineTextureMode.Stretch;
            current.startWidth = width;
            current.endWidth = width;
            current.startColor = color;
            current.endColor = color;
            current.numCapVertices = 2;
            current.numCornerVertices = 2;
            current.sortingLayerName = sortingLayerName;
            current.sortingOrder = order;
            if (runtimeMaterial != null)
                current.sharedMaterial = runtimeMaterial;
            return current;
        }

        private void UpdateGeometry()
        {
            if (polePivot == null || grabPoint == null)
                return;

            if (poleLine != null && poleVisual != null)
            {
                poleLine.positionCount = 2;
                poleLine.SetPosition(0, poleVisual.InverseTransformPoint(polePivot.position));
                poleLine.SetPosition(1, poleVisual.InverseTransformPoint(grabPoint.position));
            }

            if (flagLine != null && flagVisual != null)
            {
                flagLine.positionCount = 4;
                flagLine.SetPosition(0, Vector3.zero);
                flagLine.SetPosition(1, new Vector3(0.7f, -0.2f, 0f));
                flagLine.SetPosition(2, new Vector3(0f, -0.45f, 0f));
                flagLine.SetPosition(3, Vector3.zero);
            }

            if (baseLine != null)
            {
                baseLine.positionCount = 2;
                baseLine.SetPosition(0, new Vector3(0f, -0.35f, 0f));
                baseLine.SetPosition(1, new Vector3(0f, 0.35f, 0f));
            }
        }

        private void SetEnabled(bool value)
        {
            if (poleLine != null)
                poleLine.enabled = value;
            if (flagLine != null)
                flagLine.enabled = value;
            if (baseLine != null)
                baseLine.enabled = value;
        }
    }
}
