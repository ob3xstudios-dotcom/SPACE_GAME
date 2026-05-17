using System;
using UnityEngine;

namespace Game.Systems
{
    [DisallowMultipleComponent]
    public class LayeredBackgroundTiler : MonoBehaviour
    {
        [Serializable]
        public class TiledLayer
        {
            public Transform layerRoot;
            public SpriteRenderer sourceRenderer;
            public bool enableTiling;
            [Min(1)] public int repeatCount = 3;
            [Min(0f)] public float tileWidth = 0f;
            [Range(0f, 1f)] public float parallaxFactor = 0f;
            public Vector2 offset;
        }

        private const string TilePrefix = "__Tile_";

        [Header("Coverage")]
        [SerializeField, Min(0f)] private float coverageWidth = 0f;
        [SerializeField, Min(0)] private int coveragePaddingTiles = 2;
        [SerializeField] private Camera targetCamera;

        [Header("Sorting")]
        [SerializeField] private string sortingLayerName = "Background";

        [Header("Parallax")]
        [SerializeField] private bool syncParallaxBackground = true;
        [SerializeField] private ParallaxBackground parallaxBackground;

        [Header("Layers")]
        [SerializeField] private TiledLayer[] layers;

        [ContextMenu("Rebuild Tiles Manually")]
        public void Rebuild()
        {
            ResolveRefs();
            if (layers == null) return;

            for (int i = 0; i < layers.Length; i++)
                RebuildLayer(layers[i]);

            SyncParallax();
        }

        [ContextMenu("Clear Generated Tiles")]
        public void ClearGeneratedTiles()
        {
            if (layers == null) return;

            for (int i = 0; i < layers.Length; i++)
            {
                TiledLayer layer = layers[i];
                if (layer == null || layer.layerRoot == null) continue;

                ClearGeneratedTiles(layer.layerRoot);

                if (layer.sourceRenderer != null)
                    layer.sourceRenderer.enabled = true;
            }
        }

        private void ResolveRefs()
        {
            if (targetCamera == null && Camera.main != null)
                targetCamera = Camera.main;

            if (parallaxBackground == null)
                parallaxBackground = GetComponent<ParallaxBackground>();
        }

        private void RebuildLayer(TiledLayer layer)
        {
            if (layer == null || layer.layerRoot == null || layer.sourceRenderer == null) return;

            ClearGeneratedTiles(layer.layerRoot);
            layer.sourceRenderer.sortingLayerName = sortingLayerName;

            if (!layer.enableTiling)
            {
                layer.sourceRenderer.enabled = true;
                return;
            }

            float tileWidth = ResolveTileWidth(layer);
            int count = ResolveRepeatCount(layer, tileWidth);
            if (count <= 0 || tileWidth <= 0f) return;

            layer.sourceRenderer.enabled = false;

            int center = count / 2;
            for (int i = 0; i < count; i++)
            {
                SpriteRenderer tileRenderer = CreateTile(layer, i);

                if (tileRenderer == null) continue;

                CopyRenderer(layer.sourceRenderer, tileRenderer);
                tileRenderer.sortingLayerName = sortingLayerName;
                tileRenderer.transform.localPosition = new Vector3((i - center) * tileWidth + layer.offset.x, layer.offset.y, 0f);
                tileRenderer.transform.localRotation = Quaternion.identity;
                tileRenderer.transform.localScale = Vector3.one;
            }
        }

        private SpriteRenderer CreateTile(TiledLayer layer, int index)
        {
            GameObject tile = new GameObject($"{TilePrefix}{index:00}");
            tile.transform.SetParent(layer.layerRoot, false);
            tile.layer = layer.layerRoot.gameObject.layer;
            return tile.AddComponent<SpriteRenderer>();
        }

        private void CopyRenderer(SpriteRenderer source, SpriteRenderer target)
        {
            if (source == null || target == null) return;

            target.sprite = source.sprite;
            target.color = source.color;
            target.flipX = source.flipX;
            target.flipY = source.flipY;
            target.enabled = true;
            target.drawMode = source.drawMode;
            target.size = source.size;
            target.tileMode = source.tileMode;
            target.maskInteraction = source.maskInteraction;
            target.sortingOrder = source.sortingOrder;
            target.sharedMaterial = source.sharedMaterial;
        }

        private float ResolveTileWidth(TiledLayer layer)
        {
            if (layer.tileWidth > 0f) return layer.tileWidth;
            if (layer.sourceRenderer == null) return 0f;
            if (layer.sourceRenderer.drawMode != SpriteDrawMode.Simple)
                return Mathf.Max(0f, layer.sourceRenderer.size.x);

            return layer.sourceRenderer.sprite != null ? Mathf.Max(0f, layer.sourceRenderer.sprite.bounds.size.x) : 0f;
        }

        private int ResolveRepeatCount(TiledLayer layer, float tileWidth)
        {
            int count = Mathf.Max(1, layer.repeatCount);
            float width = ResolveCoverageWidth();

            if (width > 0f && tileWidth > 0f)
            {
                int coverageCount = Mathf.CeilToInt(width / tileWidth) + coveragePaddingTiles;
                count = Mathf.Max(count, coverageCount);
            }

            return count % 2 == 0 ? count + 1 : count;
        }

        private float ResolveCoverageWidth()
        {
            if (coverageWidth > 0f) return coverageWidth;
            if (targetCamera == null || !targetCamera.orthographic) return 0f;
            return targetCamera.orthographicSize * 2f * targetCamera.aspect;
        }

        private void ClearGeneratedTiles(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (!child.name.StartsWith(TilePrefix, StringComparison.Ordinal)) continue;

                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void SyncParallax()
        {
            if (!syncParallaxBackground || parallaxBackground == null || layers == null) return;

            Transform[] layerTransforms = new Transform[layers.Length];
            float[] parallaxFactors = new float[layers.Length];

            for (int i = 0; i < layers.Length; i++)
            {
                layerTransforms[i] = layers[i]?.layerRoot;
                parallaxFactors[i] = layers[i]?.parallaxFactor ?? 0f;
            }

            parallaxBackground.SetLayers(layerTransforms, parallaxFactors);
        }
    }
}
