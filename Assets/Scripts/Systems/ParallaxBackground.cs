using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.Systems
{
    public class ParallaxBackground : MonoBehaviour
    {
        [Serializable]
        public class ParallaxLayer
        {
            public Transform layer;
            [FormerlySerializedAs("parallaxFactor")]
            [Range(0f, 1f)] public float horizontalFactor;
            public bool useSeparateVerticalFactor;
            [Range(0f, 1f)] public float verticalFactor;

            [NonSerialized] public Vector3 startPosition;
        }

        [SerializeField] private Transform targetCamera;

        [Header("Parallax Settings")]
        [SerializeField] private ParallaxLayer[] layers;

        private Vector3 cameraStartPosition;
        private bool initialized;

        private void Awake()
        {
            ResolveCamera();
            CacheStartPositions();
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                ResolveCamera();
                CacheStartPositions();
            }

            if (!initialized || targetCamera == null) return;

            Vector3 cameraDelta = targetCamera.position - cameraStartPosition;

            for (int i = 0; i < layers.Length; i++)
            {
                ParallaxLayer layer = layers[i];
                if (layer == null || layer.layer == null) continue;

                float appliedVerticalFactor = layer.useSeparateVerticalFactor
                    ? layer.verticalFactor
                    : layer.horizontalFactor;
                Vector3 offset = new Vector3(
                    cameraDelta.x * layer.horizontalFactor,
                    cameraDelta.y * appliedVerticalFactor,
                    0f);
                layer.layer.position = new Vector3(
                    layer.startPosition.x + offset.x,
                    layer.startPosition.y + offset.y,
                    layer.startPosition.z);
            }
        }

        private void ResolveCamera()
        {
            if (targetCamera != null) return;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                targetCamera = mainCamera.transform;
        }

        private void CacheStartPositions()
        {
            if (targetCamera == null || layers == null)
            {
                initialized = false;
                return;
            }

            cameraStartPosition = targetCamera.position;

            for (int i = 0; i < layers.Length; i++)
            {
                ParallaxLayer layer = layers[i];
                if (layer == null || layer.layer == null) continue;

                layer.startPosition = layer.layer.position;
            }

            initialized = true;
        }

        public void SetLayers(Transform[] layerTransforms, float[] parallaxFactors)
        {
            if (layerTransforms == null)
            {
                layers = Array.Empty<ParallaxLayer>();
                initialized = false;
                return;
            }

            layers = new ParallaxLayer[layerTransforms.Length];
            for (int i = 0; i < layerTransforms.Length; i++)
            {
                layers[i] = new ParallaxLayer
                {
                    layer = layerTransforms[i],
                    horizontalFactor = parallaxFactors != null && i < parallaxFactors.Length ? parallaxFactors[i] : 0f
                };
            }

            CacheStartPositions();
        }
    }
}
