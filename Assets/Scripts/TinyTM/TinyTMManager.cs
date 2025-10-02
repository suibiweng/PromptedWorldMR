using Unity.InferenceEngine;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine.Networking;

namespace TinyTM
{
    [DisallowMultipleComponent]
    public class TinyTMManager : MonoBehaviour
    {
        [Header("Model (Resources/Models/mobilenet_feat.onnx)")]
        [SerializeField] private TextAsset onnxModel;     // or load via StreamingAssets (see methods below)
        [SerializeField] private int inputW = 224;
        [SerializeField] private int inputH = 224;
        [SerializeField] private string inputName = "input";
        [SerializeField] private string outputName = "embedding";
        [SerializeField] private BackendType backend = BackendType.GPUCompute;

        [Header("Camera/Source")]
        public RenderTexture sourceRT;
        public Texture2D sourceTexture;
        [Range(1, 60)] public int updatesPerSecond = 15;

        [Header("Classes and States")]
        public List<string> classLabels = new() { "R0", "R90", "R180", "R270" };
        public List<string> classStates = new() { "STATE_0", "STATE_90", "STATE_180", "STATE_270" };

        [Header("Classifier")]
        [SerializeField] private int k = 1;
        [Range(0f, 1f)] public float minConfidence = 0.25f;
        [SerializeField] private int smoothingWindow = 5;
        public bool useMeanPerClass = false;

        [Header("Lua bridge (optional)")]
        public LuaBehaviour lua;
        public string luaFuncOnState = "on_state";

        [Header("Debug")]
        public string currentLabel;
        public string currentState;
        public float currentConfidence;
        public int totalSamples;

        private EmbeddingBackboneSentis backbone;
        private EmbeddingClassifier knn;
        private readonly Queue<string> recent = new Queue<string>();
        private float timer;

        string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "tiny_tm.json");

        void Awake()
        {
            if (!onnxModel)
            {
                var ta = Resources.Load<TextAsset>("Models/mobilenet_feat");
                onnxModel = ta;
            }
            backbone = new EmbeddingBackboneSentis(onnxModel, inputW, inputH, inputName, outputName, backend);
            knn = new EmbeddingClassifier(k);

            LoadDatasetFromFile(SavePath);
        }

        void OnDestroy() => backbone?.Dispose();

        void Update()
        {
            timer += Time.deltaTime;
            float interval = 1f / Mathf.Max(1, updatesPerSecond);
            if (timer < interval) return;
            timer = 0f;

            var emb = CaptureEmbedding();
            if (emb == null || knn.Count == 0) return;

            float topSim;
            var label = knn.Predict(emb, out topSim);
            currentConfidence = topSim;

            if (string.IsNullOrEmpty(label) || topSim < minConfidence)
            {
                UpdateState(null);
                return;
            }

            EnqueueRecent(label);
            var smoothed = Majority(recent);
            if (smoothed != currentLabel)
            {
                currentLabel = smoothed;
                UpdateState(MapLabelToState(currentLabel));
            }
        }

        float[] CaptureEmbedding()
        {
            if (sourceRT) return backbone.Embed(sourceRT);
            var tex = GetCurrentTexture();
            if (tex) return backbone.Embed(tex);
            return null;
        }

        public void AddSampleByLabel(string label)
        {
            var emb = CaptureEmbedding();
            if (emb == null) return;
            if (useMeanPerClass) ReplaceWithMean(label, emb);
            else { knn.AddSample(label, emb); totalSamples++; }
        }

        void ReplaceWithMean(string label, float[] newEmb)
        {
            var field = typeof(EmbeddingClassifier).GetField("_samples",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = field.GetValue(knn) as List<EmbeddingClassifier.Sample>;
            var same = list.Where(s => s.label == label).ToList();
            if (same.Count == 0)
            {
                knn.AddSample(label, newEmb); totalSamples++;
                return;
            }
            int dim = same[0].vec.Length;
            var acc = new float[dim];
            foreach (var s in same)
                for (int i = 0; i < dim; i++) acc[i] += s.vec[i];
            for (int i = 0; i < dim; i++) acc[i] = (acc[i] + newEmb[i]) / (same.Count + 1);
            list.RemoveAll(s => s.label == label);
            knn.AddSample(label, acc);
            totalSamples = list.Count;
        }

        public void AddSampleByIndex(int labelIndex)
        {
            if (labelIndex < 0 || labelIndex >= classLabels.Count) return;
            AddSampleByLabel(classLabels[labelIndex]);
        }

        [ContextMenu("Save Dataset")]
        public void SaveDataset()
        {
            TinyTMDataset.Save(SavePath, knn.Snapshot());
            Debug.Log("TinyTM dataset saved to: " + SavePath);
        }

        public void LoadDatasetFromFile(string path)
        {
            var loaded = TinyTMDataset.Load(path);
            var field = typeof(EmbeddingClassifier).GetField("_samples",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = field.GetValue(knn) as List<EmbeddingClassifier.Sample>;
            list.Clear();
            foreach (var s in loaded) list.Add(s);
            totalSamples = list.Count;
        }

        public void ReloadModelFromResources(string resourcesPathNoExt)
        {
            var ta = Resources.Load<TextAsset>(resourcesPathNoExt);
            if (!ta) { Debug.LogError("Model not found in Resources: " + resourcesPathNoExt); return; }
            backbone.Reload(ta, inputW, inputH, backend);
            Debug.Log("Reloaded model from Resources/" + resourcesPathNoExt + ".onnx");
        }

        public void ReloadModelFromStreamingAssets(string relativePath)
        {
            StartCoroutine(Co_ReloadModelFromStreamingAssets(relativePath));
        }

        IEnumerator Co_ReloadModelFromStreamingAssets(string relativePath)
        {
            string full = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
#if UNITY_ANDROID && !UNITY_EDITOR
            using var req = UnityWebRequest.Get(full);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to load ONNX from StreamingAssets: " + req.error);
                yield break;
            }
            var bytes = req.downloadHandler.data;
            backbone.Reload(bytes, inputW, inputH, backend);
            Debug.Log($"Reloaded model from StreamingAssets: {relativePath} ({bytes.Length} bytes)");
#else
            backbone.ReloadFromPath(full, inputW, inputH, backend);
            yield return null;
#endif
        }

        Texture2D GetCurrentTexture() => sourceTexture;

        string MapLabelToState(string label)
        {
            int i = classLabels.IndexOf(label);
            if (i < 0 || i >= classStates.Count) return label;
            return classStates[i];
        }

        void EnqueueRecent(string label)
        {
            recent.Enqueue(label);
            while (recent.Count > Mathf.Max(1, smoothingWindow)) recent.Dequeue();
        }

        static string Majority(Queue<string> q)
        {
            var c = new Dictionary<string, int>();
            foreach (var s in q) { if (!c.ContainsKey(s)) c[s] = 0; c[s]++; }
            string best = null; int bc = -1;
            foreach (var kv in c) if (kv.Value > bc) { best = kv.Key; bc = kv.Value; }
            return best;
        }

        void UpdateState(string newState)
        {
            if (newState == currentState) return;
            currentState = newState;
            // if (lua && !string.IsNullOrEmpty(currentState))
            // {
            //     if (lua.HasFunction(luaFuncOnState))
            //         lua.Call(luaFuncOnState, currentState);
            // }
        }



        public void ClearSamples()
{
    knn.Clear();
    totalSamples = 0;

    // also clear the saved dataset on disk (optional)
    var path = System.IO.Path.Combine(Application.persistentDataPath, "tiny_tm.json");
    if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);

    // reset UI/debug fields
    currentLabel = null;
    currentState = null;
    currentConfidence = 0f;
}

    }
}
