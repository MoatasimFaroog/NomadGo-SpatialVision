using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace NomadGo.Vision
{
    public class ONNXInferenceEngine : MonoBehaviour
    {
        private const string CanonicalModelRelativePath = "Models/yolov8n.onnx";
        private const string CanonicalLabelsRelativePath = "Models/labels.txt";

        private int inputWidth = 640;
        private int inputHeight = 640;
        private float confidenceThreshold = 0.45f;
        private float nmsThreshold = 0.5f;
        private int maxDetections = 100;

        private string[] labels = Array.Empty<string>();
        private bool isLoaded;
        private bool isLoading;
        private string lastError;
        private float lastInferenceTimeMs;

        private object runtimeModel;
        private object worker;
        private MethodInfo modelLoadFromBytes;
        private MethodInfo workerCreate;
        private MethodInfo workerExecute;
        private MethodInfo workerPeekOutput;
        private MethodInfo textureToTensor;

        public bool IsLoaded => isLoaded;
        public bool IsLoading => isLoading;
        public float LastInferenceTimeMs => lastInferenceTimeMs;
        public string LastError => lastError;

        public event Action<bool, string> OnEngineReadyChanged;

        public void Initialize(AppShell.ModelConfig config)
        {
            inputWidth = Mathf.Max(1, config.input_width);
            inputHeight = Mathf.Max(1, config.input_height);
            confidenceThreshold = Mathf.Clamp01(config.confidence_threshold);
            nmsThreshold = Mathf.Clamp01(config.nms_threshold);
            maxDetections = Mathf.Max(1, config.max_detections);

            if (!string.Equals(config.path, CanonicalModelRelativePath, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[ONNXEngine] Ignoring non-canonical model path '{config.path}'. Using StreamingAssets/{CanonicalModelRelativePath}.");
            }

            if (!string.Equals(config.labels_path, CanonicalLabelsRelativePath, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[ONNXEngine] Ignoring non-canonical labels path '{config.labels_path}'. Using StreamingAssets/{CanonicalLabelsRelativePath}.");
            }

            if (isLoading)
            {
                return;
            }

            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            isLoaded = false;
            isLoading = true;
            lastError = null;

            yield return LoadLabelsRoutine();
            if (!string.IsNullOrEmpty(lastError))
            {
                FailInitialization(lastError);
                yield break;
            }

            byte[] modelBytes = null;
            yield return LoadStreamingAssetBytesRoutine(CanonicalModelRelativePath, bytes => modelBytes = bytes);
            if (modelBytes == null || modelBytes.Length == 0)
            {
                FailInitialization($"Model file missing or empty at StreamingAssets/{CanonicalModelRelativePath}.");
                yield break;
            }

            try
            {
                if (!InitializeSentisReflection(modelBytes))
                {
                    FailInitialization(lastError);
                    yield break;
                }

                isLoaded = true;
                isLoading = false;
                Debug.Log($"[ONNXEngine] Sentis model loaded from StreamingAssets/{CanonicalModelRelativePath}");
                OnEngineReadyChanged?.Invoke(true, null);
            }
            catch (Exception ex)
            {
                FailInitialization($"Sentis initialization failed: {ex.Message}");
            }
        }

        private void FailInitialization(string message)
        {
            lastError = message;
            isLoading = false;
            isLoaded = false;
            Debug.LogError($"[ONNXEngine] {message}");
            OnEngineReadyChanged?.Invoke(false, lastError);
        }

        private IEnumerator LoadLabelsRoutine()
        {
            string labelsContent = null;
            yield return LoadStreamingAssetTextRoutine(CanonicalLabelsRelativePath, text => labelsContent = text);

            if (string.IsNullOrWhiteSpace(labelsContent))
            {
                lastError = $"Labels file missing or empty at StreamingAssets/{CanonicalLabelsRelativePath}.";
                yield break;
            }

            labels = labelsContent
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (labels.Length == 0)
            {
                lastError = $"No labels found in StreamingAssets/{CanonicalLabelsRelativePath}.";
                yield break;
            }

            Debug.Log($"[ONNXEngine] Loaded {labels.Length} labels from StreamingAssets.");
        }

        private IEnumerator LoadStreamingAssetTextRoutine(string relativePath, Action<string> onLoaded)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

            if (fullPath.Contains("://") || fullPath.Contains(":///"))
            {
                using (UnityWebRequest request = UnityWebRequest.Get(fullPath))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        onLoaded?.Invoke(request.downloadHandler.text);
                    }
                    else
                    {
                        onLoaded?.Invoke(null);
                    }
                }
                yield break;
            }

            if (!File.Exists(fullPath))
            {
                onLoaded?.Invoke(null);
                yield break;
            }

            onLoaded?.Invoke(File.ReadAllText(fullPath));
        }

        private IEnumerator LoadStreamingAssetBytesRoutine(string relativePath, Action<byte[]> onLoaded)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

            if (fullPath.Contains("://") || fullPath.Contains(":///"))
            {
                using (UnityWebRequest request = UnityWebRequest.Get(fullPath))
                {
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        onLoaded?.Invoke(request.downloadHandler.data);
                    }
                    else
                    {
                        onLoaded?.Invoke(null);
                    }
                }
                yield break;
            }

            if (!File.Exists(fullPath))
            {
                onLoaded?.Invoke(null);
                yield break;
            }

            onLoaded?.Invoke(File.ReadAllBytes(fullPath));
        }

        private bool InitializeSentisReflection(byte[] modelBytes)
        {
            Assembly sentisAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

            if (sentisAssembly == null)
            {
                lastError = "Unity Sentis assembly (Unity.Sentis) not found. Ensure com.unity.sentis is installed.";
                return false;
            }

            Type modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
            Type workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");
            Type backendType = sentisAssembly.GetType("Unity.Sentis.BackendType");
            Type textureConverterType = sentisAssembly.GetType("Unity.Sentis.TextureConverter");
            Type textureType = typeof(Texture);

            if (modelLoaderType == null || workerFactoryType == null || backendType == null || textureConverterType == null)
            {
                lastError = "Unity Sentis API types were not found. Check Sentis package integrity/version.";
                return false;
            }

            modelLoadFromBytes = modelLoaderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Load" &&
                                     m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType == typeof(byte[]));

            if (modelLoadFromBytes == null)
            {
                lastError = "Sentis ModelLoader.Load(byte[]) is unavailable. This project requires runtime ONNX loading from StreamingAssets.";
                return false;
            }

            runtimeModel = modelLoadFromBytes.Invoke(null, new object[] { modelBytes });
            if (runtimeModel == null)
            {
                lastError = "Sentis returned null runtime model.";
                return false;
            }

            workerCreate = workerFactoryType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "CreateWorker" && m.GetParameters().Length == 2);

            if (workerCreate == null)
            {
                lastError = "Sentis WorkerFactory.CreateWorker overload not found.";
                return false;
            }

            object backend = Enum.Parse(backendType, SystemInfo.supportsComputeShaders ? "GPUCompute" : "CPU");
            worker = workerCreate.Invoke(null, new[] { backend, runtimeModel });

            if (worker == null)
            {
                lastError = "Sentis worker creation returned null.";
                return false;
            }

            Type workerType = worker.GetType();
            workerExecute = workerType.GetMethod("Execute", new[] { sentisAssembly.GetType("Unity.Sentis.Tensor") })
                ?? workerType.GetMethod("Execute", new[] { sentisAssembly.GetType("Unity.Sentis.TensorFloat") })
                ?? workerType.GetMethods().FirstOrDefault(m => m.Name == "Execute" && m.GetParameters().Length == 1);

            workerPeekOutput = workerType.GetMethods().FirstOrDefault(m => m.Name == "PeekOutput" && m.GetParameters().Length == 0);

            textureToTensor = textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture), typeof(int), typeof(int), typeof(int) })
                ?? textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture) })
                ?? textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "ToTensor") return false;
                        ParameterInfo[] p = m.GetParameters();
                        return p.Length >= 1 && p[0].ParameterType == textureType;
                    });

            if (workerExecute == null || workerPeekOutput == null || textureToTensor == null)
            {
                lastError = "Sentis execution APIs were not found (Execute/PeekOutput/TextureConverter.ToTensor).";
                return false;
            }

            return true;
        }

        public List<DetectionResult> RunInference(Texture2D frame)
        {
            if (!isLoaded || worker == null)
            {
                return new List<DetectionResult>();
            }

            if (frame == null)
            {
                return new List<DetectionResult>();
            }

            object inputTensor = null;
            object outputTensor = null;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                object[] args = BuildTextureToTensorArgs(frame, textureToTensor);
                inputTensor = textureToTensor.Invoke(null, args);

                workerExecute.Invoke(worker, new[] { inputTensor });
                outputTensor = workerPeekOutput.Invoke(worker, null);
                float[] outputData = ExtractTensorData(outputTensor);
                int[] shape = ExtractTensorShape(outputTensor);

                List<DetectionResult> rawDetections = ParseYoloOutput(outputData, shape, frame.width, frame.height);
                List<DetectionResult> nmsDetections = ApplyNMS(rawDetections);

                if (nmsDetections.Count > maxDetections)
                {
                    nmsDetections = nmsDetections.Take(maxDetections).ToList();
                }

                stopwatch.Stop();
                lastInferenceTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
                return nmsDetections;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Inference failed: {ex.Message}");
                return new List<DetectionResult>();
            }
            finally
            {
                stopwatch.Stop();
                lastInferenceTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;

                if (inputTensor is IDisposable disposableInput)
                {
                    disposableInput.Dispose();
                }

                if (outputTensor is IDisposable disposableOutput)
                {
                    disposableOutput.Dispose();
                }
            }
        }

        private object[] BuildTextureToTensorArgs(Texture2D frame, MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();

            if (parameters.Length == 1)
            {
                return new object[] { frame };
            }

            if (parameters.Length == 4 &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType == typeof(int) &&
                parameters[3].ParameterType == typeof(int))
            {
                return new object[] { frame, inputWidth, inputHeight, 3 };
            }

            object[] args = new object[parameters.Length];
            args[0] = frame;
            for (int i = 1; i < parameters.Length; i++)
            {
                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Activator.CreateInstance(parameters[i].ParameterType);
            }

            return args;
        }

        private float[] ExtractTensorData(object tensor)
        {
            if (tensor == null)
            {
                throw new InvalidOperationException("Sentis output tensor is null.");
            }

            MethodInfo makeReadable = tensor.GetType().GetMethod("MakeReadable", Type.EmptyTypes);
            makeReadable?.Invoke(tensor, null);

            MethodInfo toReadOnlyArray = tensor.GetType().GetMethod("ToReadOnlyArray", Type.EmptyTypes);
            if (toReadOnlyArray == null)
            {
                throw new InvalidOperationException("Sentis output tensor does not expose ToReadOnlyArray().");
            }

            return toReadOnlyArray.Invoke(tensor, null) as float[];
        }

        private int[] ExtractTensorShape(object tensor)
        {
            if (tensor == null)
            {
                return Array.Empty<int>();
            }

            PropertyInfo shapeProp = tensor.GetType().GetProperty("shape");
            object shapeObj = shapeProp?.GetValue(tensor);
            if (shapeObj == null)
            {
                return Array.Empty<int>();
            }

            MethodInfo toArray = shapeObj.GetType().GetMethod("ToArray", Type.EmptyTypes);
            if (toArray != null)
            {
                if (toArray.Invoke(shapeObj, null) is int[] dims)
                {
                    return dims;
                }
            }

            PropertyInfo rankProp = shapeObj.GetType().GetProperty("rank");
            PropertyInfo indexer = shapeObj.GetType().GetProperty("Item");
            if (rankProp != null && indexer != null)
            {
                int rank = (int)rankProp.GetValue(shapeObj);
                int[] dims = new int[rank];
                for (int i = 0; i < rank; i++)
                {
                    dims[i] = (int)indexer.GetValue(shapeObj, new object[] { i });
                }
                return dims;
            }

            return Array.Empty<int>();
        }

        private List<DetectionResult> ParseYoloOutput(float[] output, int[] shape, int originalWidth, int originalHeight)
        {
            List<DetectionResult> detections = new List<DetectionResult>();
            if (output == null || output.Length == 0)
            {
                return detections;
            }

            int numClasses = labels.Length;
            if (numClasses <= 0)
            {
                Debug.LogError("[ONNXEngine] Labels are not loaded; cannot decode YOLO classes.");
                return detections;
            }

            if (shape.Length == 3 && shape[0] == 1)
            {
                // Common YOLOv8 output: [1,84,8400]
                if (shape[1] >= 5 && shape[2] > 0)
                {
                    DecodeYolo3DChannelFirst(output, shape[1], shape[2], numClasses, originalWidth, originalHeight, detections);
                    return detections;
                }

                // Alternate: [1,8400,84]
                if (shape[2] >= 5 && shape[1] > 0)
                {
                    DecodeYolo3DBoxFirst(output, shape[1], shape[2], numClasses, originalWidth, originalHeight, detections);
                    return detections;
                }
            }

            if (shape.Length == 2 && shape[1] >= 5)
            {
                DecodeYolo2D(output, shape[0], shape[1], numClasses, originalWidth, originalHeight, detections);
                return detections;
            }

            Debug.LogWarning($"[ONNXEngine] Unsupported output shape [{string.Join(",", shape)}], output length={output.Length}.");
            return detections;
        }

        private void DecodeYolo3DChannelFirst(float[] output, int channels, int numBoxes, int numClasses, int originalWidth, int originalHeight, List<DetectionResult> detections)
        {
            for (int i = 0; i < numBoxes; i++)
            {
                float cx = output[i + 0 * numBoxes];
                float cy = output[i + 1 * numBoxes];
                float w = output[i + 2 * numBoxes];
                float h = output[i + 3 * numBoxes];

                float bestConf = 0f;
                int bestClass = -1;

                for (int c = 0; c < numClasses && (4 + c) < channels; c++)
                {
                    float score = output[i + (4 + c) * numBoxes];
                    if (score > bestConf)
                    {
                        bestConf = score;
                        bestClass = c;
                    }
                }

                AddDetectionIfValid(cx, cy, w, h, bestClass, bestConf, originalWidth, originalHeight, detections);
            }
        }

        private void DecodeYolo3DBoxFirst(float[] output, int numBoxes, int channels, int numClasses, int originalWidth, int originalHeight, List<DetectionResult> detections)
        {
            for (int i = 0; i < numBoxes; i++)
            {
                int rowOffset = i * channels;
                float cx = output[rowOffset + 0];
                float cy = output[rowOffset + 1];
                float w = output[rowOffset + 2];
                float h = output[rowOffset + 3];

                float bestConf = 0f;
                int bestClass = -1;

                for (int c = 0; c < numClasses && (4 + c) < channels; c++)
                {
                    float score = output[rowOffset + 4 + c];
                    if (score > bestConf)
                    {
                        bestConf = score;
                        bestClass = c;
                    }
                }

                AddDetectionIfValid(cx, cy, w, h, bestClass, bestConf, originalWidth, originalHeight, detections);
            }
        }

        private void DecodeYolo2D(float[] output, int rows, int cols, int numClasses, int originalWidth, int originalHeight, List<DetectionResult> detections)
        {
            for (int i = 0; i < rows; i++)
            {
                int rowOffset = i * cols;
                float cx = output[rowOffset + 0];
                float cy = output[rowOffset + 1];
                float w = output[rowOffset + 2];
                float h = output[rowOffset + 3];

                float objectness = cols > 4 ? output[rowOffset + 4] : 1f;
                if (objectness < confidenceThreshold)
                {
                    continue;
                }

                float bestConf = 0f;
                int bestClass = -1;

                for (int c = 0; c < numClasses && (5 + c) < cols; c++)
                {
                    float score = output[rowOffset + 5 + c] * objectness;
                    if (score > bestConf)
                    {
                        bestConf = score;
                        bestClass = c;
                    }
                }

                AddDetectionIfValid(cx, cy, w, h, bestClass, bestConf, originalWidth, originalHeight, detections);
            }
        }

        private void AddDetectionIfValid(float cx, float cy, float w, float h, int classId, float confidence, int originalWidth, int originalHeight, List<DetectionResult> detections)
        {
            if (classId < 0 || confidence < confidenceThreshold)
            {
                return;
            }

            float scaleX = (float)originalWidth / inputWidth;
            float scaleY = (float)originalHeight / inputHeight;

            float x = (cx - w * 0.5f) * scaleX;
            float y = (cy - h * 0.5f) * scaleY;
            float width = w * scaleX;
            float height = h * scaleY;

            x = Mathf.Clamp(x, 0f, originalWidth - 1f);
            y = Mathf.Clamp(y, 0f, originalHeight - 1f);
            width = Mathf.Clamp(width, 1f, originalWidth - x);
            height = Mathf.Clamp(height, 1f, originalHeight - y);

            detections.Add(new DetectionResult(classId, GetLabel(classId), confidence, new Rect(x, y, width, height)));
        }

        private List<DetectionResult> ApplyNMS(List<DetectionResult> detections)
        {
            if (detections.Count == 0)
            {
                return detections;
            }

            detections.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            List<DetectionResult> kept = new List<DetectionResult>();
            bool[] suppressed = new bool[detections.Count];

            for (int i = 0; i < detections.Count; i++)
            {
                if (suppressed[i])
                {
                    continue;
                }

                kept.Add(detections[i]);

                for (int j = i + 1; j < detections.Count; j++)
                {
                    if (suppressed[j] || detections[i].classId != detections[j].classId)
                    {
                        continue;
                    }

                    if (ComputeIOU(detections[i].boundingBox, detections[j].boundingBox) > nmsThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            return kept;
        }

        public static float ComputeIOU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin);
            float y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax);
            float y2 = Mathf.Min(a.yMax, b.yMax);

            float intersectionArea = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
            float unionArea = a.width * a.height + b.width * b.height - intersectionArea;
            if (unionArea <= 0f)
            {
                return 0f;
            }

            return intersectionArea / unionArea;
        }

        public string GetLabel(int classId)
        {
            if (classId >= 0 && classId < labels.Length)
            {
                return labels[classId];
            }

            return $"class_{classId}";
        }

        private void OnDestroy()
        {
            if (worker is IDisposable workerDisposable)
            {
                workerDisposable.Dispose();
                worker = null;
            }
        }
    }
}
