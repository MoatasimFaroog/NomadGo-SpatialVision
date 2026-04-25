using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace NomadGo.Vision
{
    public class FrameProcessor : MonoBehaviour
    {
        public enum ProcessorState
        {
            NotInitialized,
            LoadingModel,
            Ready,
            Scanning,
            Error
        }

        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private ONNXInferenceEngine inferenceEngine;

        private int frameSkip;
        private int frameCounter;
        private bool isFrameInProgress;
        private List<DetectionResult> latestDetections = new List<DetectionResult>();
        private ProcessorState state = ProcessorState.NotInitialized;
        private string lastError;

        public bool IsProcessing => state == ProcessorState.Scanning;
        public bool CanStartProcessing => state == ProcessorState.Ready;
        public ProcessorState State => state;
        public string LastError => lastError;
        public List<DetectionResult> LatestDetections => latestDetections;
        public float LastInferenceTimeMs => inferenceEngine != null ? inferenceEngine.LastInferenceTimeMs : 0f;

        public delegate void DetectionsUpdatedHandler(List<DetectionResult> detections);
        public event DetectionsUpdatedHandler OnDetectionsUpdated;
        public event Action<ProcessorState, string> OnStateChanged;

        public void Initialize(AppShell.ModelConfig config)
        {
            if (inferenceEngine == null)
            {
                inferenceEngine = GetComponent<ONNXInferenceEngine>();
            }

            if (inferenceEngine == null)
            {
                inferenceEngine = gameObject.AddComponent<ONNXInferenceEngine>();
            }

            inferenceEngine.OnEngineReadyChanged -= OnEngineReadyChanged;
            inferenceEngine.OnEngineReadyChanged += OnEngineReadyChanged;

            frameSkip = Mathf.Max(0, (int)(60f / 15f) - 1);
            frameCounter = 0;
            SetState(ProcessorState.LoadingModel, null);
            inferenceEngine.Initialize(config);

            Debug.Log($"[FrameProcessor] Initializing model. Frame skip: {frameSkip}");
        }

        public void StartProcessing()
        {
            if (state == ProcessorState.Scanning)
            {
                Debug.LogWarning("[FrameProcessor] Scan already active; ignoring duplicate start.");
                return;
            }

            if (state != ProcessorState.Ready || inferenceEngine == null || !inferenceEngine.IsLoaded)
            {
                SetState(ProcessorState.Error, "Cannot start scanning: model is not ready.");
                return;
            }

            if (cameraManager == null)
            {
                SetState(ProcessorState.Error, "Cannot start scanning: ARCameraManager reference is missing.");
                return;
            }

            cameraManager.frameReceived -= OnCameraFrameReceived;
            cameraManager.frameReceived += OnCameraFrameReceived;
            frameCounter = 0;
            isFrameInProgress = false;
            SetState(ProcessorState.Scanning, null);

            Debug.Log("[FrameProcessor] Scanning started.");
        }

        public void StopProcessing()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }

            isFrameInProgress = false;

            if (state == ProcessorState.Scanning)
            {
                SetState(ProcessorState.Ready, null);
            }

            Debug.Log("[FrameProcessor] Scanning stopped.");
        }

        private void OnEngineReadyChanged(bool ready, string error)
        {
            if (ready)
            {
                SetState(ProcessorState.Ready, null);
                return;
            }

            SetState(ProcessorState.Error, string.IsNullOrWhiteSpace(error)
                ? "Inference backend failed to initialize."
                : error);
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (state != ProcessorState.Scanning || isFrameInProgress)
            {
                return;
            }

            frameCounter++;
            if (frameCounter % (frameSkip + 1) != 0)
            {
                return;
            }

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            {
                return;
            }

            isFrameInProgress = true;
            Texture2D frameTexture = null;

            try
            {
                frameTexture = ConvertCpuImageToTexture(cpuImage);
                if (frameTexture == null)
                {
                    return;
                }

                ProcessFrame(frameTexture);
            }
            catch (Exception ex)
            {
                SetState(ProcessorState.Error, $"Camera frame processing failed: {ex.Message}");
            }
            finally
            {
                cpuImage.Dispose();
                if (frameTexture != null)
                {
                    Destroy(frameTexture);
                }

                isFrameInProgress = false;
            }
        }

        private Texture2D ConvertCpuImageToTexture(XRCpuImage cpuImage)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            int size = cpuImage.GetConvertedDataSize(conversionParams);
            NativeArray<byte> buffer = new NativeArray<byte>(size, Allocator.Temp);

            try
            {
                cpuImage.Convert(conversionParams, buffer);

                Texture2D texture = new Texture2D(
                    conversionParams.outputDimensions.x,
                    conversionParams.outputDimensions.y,
                    conversionParams.outputFormat,
                    false
                );

                texture.LoadRawTextureData(buffer);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private void ProcessFrame(Texture2D frame)
        {
            if (inferenceEngine == null || !inferenceEngine.IsLoaded)
            {
                SetState(ProcessorState.Error, "Inference engine is unavailable while scanning.");
                return;
            }

            List<DetectionResult> detections = inferenceEngine.RunInference(frame) ?? new List<DetectionResult>();
            latestDetections = detections;
            OnDetectionsUpdated?.Invoke(latestDetections);

            if (detections.Count > 0)
            {
                Debug.Log($"[FrameProcessor] Detections: {detections.Count}, Inference={inferenceEngine.LastInferenceTimeMs:F1}ms");
            }
        }

        private void SetState(ProcessorState newState, string error)
        {
            state = newState;
            lastError = error;

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogError($"[FrameProcessor] {error}");
            }

            OnStateChanged?.Invoke(state, lastError);
        }

        private void OnDestroy()
        {
            StopProcessing();

            if (inferenceEngine != null)
            {
                inferenceEngine.OnEngineReadyChanged -= OnEngineReadyChanged;
            }
        }
    }
}
