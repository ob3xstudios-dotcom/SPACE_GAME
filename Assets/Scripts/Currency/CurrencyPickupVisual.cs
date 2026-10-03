using System.Collections.Generic;
using UnityEngine;

namespace Game.Currency
{
    public enum CurrencyVisualKind
    {
        Coin,
        Bag
    }

    /// <summary>
    /// Replaceable procedural placeholder. It owns no colliders or economy state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CurrencyPickupVisual : MonoBehaviour
    {
        [SerializeField] private CurrencyVisualKind kind = CurrencyVisualKind.Coin;
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder;
        [SerializeField] private Color coinColor = new Color(1f, 0.78f, 0.08f, 1f);
        [SerializeField] private Color bagColor = new Color(0.62f, 0.34f, 0.12f, 1f);
        [SerializeField] private Color detailColor = new Color(1f, 0.9f, 0.45f, 1f);
        [SerializeField, Min(0.01f)] private float lineWidth = 0.09f;
        [SerializeField] private bool enableMotion = true;
        [SerializeField, Min(0f)] private float floatAmplitude = 0.06f;
        [SerializeField, Min(0f)] private float floatFrequency = 1.5f;

        private readonly List<GameObject> generatedObjects = new List<GameObject>();
        private Material runtimeMaterial;
        private Vector3 baseLocalPosition;

        private void Awake()
        {
            baseLocalPosition = transform.localPosition;
            RebuildVisual();
        }

        private void LateUpdate()
        {
            if (!enableMotion)
                return;

            float offset = Mathf.Sin(Time.time * floatFrequency) * floatAmplitude;
            transform.localPosition = baseLocalPosition + Vector3.up * offset;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        public void Configure(CurrencyVisualKind visualKind)
        {
            Configure(visualKind, sortingLayerName, sortingOrder);
        }

        public void Configure(
            CurrencyVisualKind visualKind,
            string targetSortingLayerName,
            int targetSortingOrder)
        {
            string normalizedLayerName = string.IsNullOrWhiteSpace(targetSortingLayerName)
                ? "Default"
                : targetSortingLayerName.Trim();
            if (kind == visualKind &&
                sortingLayerName == normalizedLayerName &&
                sortingOrder == targetSortingOrder &&
                generatedObjects.Count > 0)
            {
                return;
            }

            kind = visualKind;
            sortingLayerName = normalizedLayerName;
            sortingOrder = targetSortingOrder;
            if (Application.isPlaying)
                RebuildVisual();
        }

        private void RebuildVisual()
        {
            for (int i = 0; i < generatedObjects.Count; i++)
            {
                if (generatedObjects[i] != null)
                    Destroy(generatedObjects[i]);
            }
            generatedObjects.Clear();

            if (runtimeMaterial == null)
                runtimeMaterial = CreateRuntimeMaterial();

            if (kind == CurrencyVisualKind.Bag)
                CreateBagVisual();
            else
                CreateCoinVisual();
        }

        private void CreateCoinVisual()
        {
            const int segments = 20;
            LineRenderer outline = CreateLine("CoinOutline", sortingOrder, segments, true, coinColor);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                outline.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.38f);
            }

            LineRenderer detail = CreateLine("CoinDetail", sortingOrder + 1, 2, false, detailColor);
            detail.SetPosition(0, new Vector3(0f, -0.2f, 0f));
            detail.SetPosition(1, new Vector3(0f, 0.2f, 0f));
        }

        private void CreateBagVisual()
        {
            Vector3[] points =
            {
                new Vector3(-0.45f, -0.48f, 0f),
                new Vector3(-0.58f, -0.06f, 0f),
                new Vector3(-0.40f, 0.32f, 0f),
                new Vector3(-0.16f, 0.44f, 0f),
                new Vector3(-0.28f, 0.65f, 0f),
                new Vector3(0.28f, 0.65f, 0f),
                new Vector3(0.16f, 0.44f, 0f),
                new Vector3(0.40f, 0.32f, 0f),
                new Vector3(0.58f, -0.06f, 0f),
                new Vector3(0.45f, -0.48f, 0f)
            };

            LineRenderer outline = CreateLine("BagOutline", sortingOrder, points.Length, false, bagColor);
            outline.SetPositions(points);

            LineRenderer baseLine = CreateLine("BagBase", sortingOrder, 2, false, bagColor);
            baseLine.SetPosition(0, points[0]);
            baseLine.SetPosition(1, points[points.Length - 1]);

            LineRenderer tie = CreateLine("BagTie", sortingOrder + 1, 2, false, detailColor);
            tie.SetPosition(0, new Vector3(-0.30f, 0.43f, 0f));
            tie.SetPosition(1, new Vector3(0.30f, 0.43f, 0f));
        }

        private LineRenderer CreateLine(
            string objectName,
            int order,
            int pointCount,
            bool loop,
            Color color)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.layer = gameObject.layer;
            lineObject.transform.SetParent(transform, false);
            generatedObjects.Add(lineObject);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = pointCount;
            line.loop = loop;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = lineWidth;
            line.startColor = color;
            line.endColor = color;
            line.sortingLayerName = sortingLayerName;
            line.sortingOrder = order;
            if (runtimeMaterial != null)
                line.sharedMaterial = runtimeMaterial;
            return line;
        }

        private static Material CreateRuntimeMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            return shader != null ? new Material(shader) : null;
        }
    }
}
