using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace NomadGo.Spatial
{
    public class DepthEstimator : MonoBehaviour
    {
        [SerializeField] private AROcclusionManager occlusionManager;

        private bool depthAvailable;
        private bool loggedUnsupported;

        public bool DepthAvailable => depthAvailable;

        private void Update()
        {
            depthAvailable = occlusionManager != null &&
                             occlusionManager.descriptor != null &&
                             occlusionManager.descriptor.supportsEnvironmentDepthImage;
        }

        public float EstimateDepthAtScreenPoint(Vector2 normalizedScreenPoint)
        {
            if (!depthAvailable || occlusionManager == null)
            {
                return -1f;
            }

            if (!occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage depthImage))
            {
                if (!loggedUnsupported)
                {
                    loggedUnsupported = true;
                    Debug.LogWarning("[DepthEstimator] Environment depth CPU image unavailable on this frame/device.");
                }

                return -1f;
            }

            try
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(normalizedScreenPoint.x * (depthImage.width - 1)), 0, depthImage.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(normalizedScreenPoint.y * (depthImage.height - 1)), 0, depthImage.height - 1);

                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(x, y, 1, 1),
                    outputDimensions = new Vector2Int(1, 1),
                    outputFormat = TextureFormat.RFloat,
                    transformation = XRCpuImage.Transformation.None
                };

                int size = depthImage.GetConvertedDataSize(conversionParams);
                if (size < sizeof(float))
                {
                    return -1f;
                }

                NativeArray<byte> depthBytes = new NativeArray<byte>(size, Allocator.Temp);
                try
                {
                    depthImage.Convert(conversionParams, depthBytes);
                    byte[] managed = depthBytes.ToArray();
                    float depthMeters = BitConverter.ToSingle(managed, 0);

                    if (float.IsNaN(depthMeters) || float.IsInfinity(depthMeters) || depthMeters <= 0f)
                    {
                        return -1f;
                    }

                    return depthMeters;
                }
                finally
                {
                    depthBytes.Dispose();
                }
            }
            finally
            {
                depthImage.Dispose();
            }
        }

        public float EstimateDepthAtBoundingBox(Rect boundingBox)
        {
            Vector2 center = new Vector2(
                (boundingBox.xMin + boundingBox.xMax) / 2f,
                (boundingBox.yMin + boundingBox.yMax) / 2f
            );

            float centerDepth = EstimateDepthAtScreenPoint(center);
            if (centerDepth < 0f)
            {
                return -1f;
            }

            float topDepth = EstimateDepthAtScreenPoint(new Vector2(center.x, boundingBox.yMin));
            float bottomDepth = EstimateDepthAtScreenPoint(new Vector2(center.x, boundingBox.yMax));

            float total = centerDepth;
            int count = 1;

            if (topDepth > 0f)
            {
                total += topDepth;
                count++;
            }

            if (bottomDepth > 0f)
            {
                total += bottomDepth;
                count++;
            }

            return total / count;
        }
    }
}
