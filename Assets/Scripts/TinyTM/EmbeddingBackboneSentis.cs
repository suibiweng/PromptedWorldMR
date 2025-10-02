// Assets/Scripts/TinyTM/EmbeddingBackboneSentis.cs
// Unity 6: Inference Engine only (com.unity.ai.inference)
using Unity.InferenceEngine;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace TinyTM
{
    public sealed class EmbeddingBackboneSentis : System.IDisposable
    {
        private Model _model;
        private Worker _worker;
        private readonly string _inputName, _outputName;
        private int _w, _h;
        private BackendType _backend;

        public EmbeddingBackboneSentis(TextAsset onnx,
            int w = 224, int h = 224, string inputName = "input", string outputName = "embedding",
            BackendType backend = BackendType.GPUCompute)
        {
            _inputName = inputName; _outputName = outputName;
            Reload(onnx, w, h, backend);
        }

        // Load from a TextAsset containing ONNX bytes (e.g., from Resources)
        public void Reload(TextAsset onnx, int w, int h, BackendType backend)
        {
            Dispose();
            _w = w; _h = h; _backend = backend;

            // Write ONNX bytes to a temporary file and load by path
            string tmpPath = Path.Combine(Application.persistentDataPath, "tiny_tm_model.onnx");
            File.WriteAllBytes(tmpPath, onnx.bytes);
            _model = ModelLoader.Load(tmpPath);
            _worker = new Worker(_model, _backend);
        }

        // Load from raw ONNX bytes (downloaded / StreamingAssets-copied)
        public void Reload(byte[] onnxBytes, int w, int h, BackendType backend)
        {
            Dispose();
            _w = w; _h = h; _backend = backend;
            string tmpPath = Path.Combine(Application.persistentDataPath, "tiny_tm_hotload.onnx");
            File.WriteAllBytes(tmpPath, onnxBytes);
            _model = ModelLoader.Load(tmpPath);
            _worker = new Worker(_model, _backend);
        }

        // Load directly from a filesystem path (persistent/StreamingAssets copy)
        public void ReloadFromPath(string fullPath, int w, int h, BackendType backend)
        {
            Dispose();
            _w = w; _h = h; _backend = backend;
            _model = ModelLoader.Load(fullPath);
            _worker = new Worker(_model, _backend);
        }

public float[] Embed(Texture2D tex)
{
    using var t = Preprocess(tex, _w, _h);
    _worker.SetInput(_inputName, t);   // or: _worker.Schedule(t) if single unnamed input
    _worker.Schedule();
    using var o = _worker.PeekOutput(_outputName) as Tensor<float>;
    return o.DownloadToArray();        // returns float[] (blocking)
}

public float[] Embed(RenderTexture rt)
{
    using var t = Preprocess(rt, _w, _h);
    _worker.SetInput(_inputName, t);
    _worker.Schedule();
    using var o = _worker.PeekOutput(_outputName) as Tensor<float>;
    return o.DownloadToArray();        // returns float[] (blocking)
}


        // ARGB32 -> float CHW [0,1]
static Tensor<float> Preprocess(Texture2D tex, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var t = Preprocess(rt, w, h);
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        static Tensor<float> Preprocess(RenderTexture src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0); tmp.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var chw = new float[3 * h * w];
            var px = tmp.GetPixels32();
            Object.Destroy(tmp);

            for (int y=0; y<h; y++)
            for (int x=0; x<w; x++)
            {
                var p = px[y*w + x];
                int idx = y*w + x;
                chw[0*h*w + idx] = p.r / 255f;
                chw[1*h*w + idx] = p.g / 255f;
                chw[2*h*w + idx] = p.b / 255f;
            }
            return new Tensor<float>(new TensorShape(1, 3, h, w), chw);
        }

        public void Dispose()
        {
            _worker?.Dispose();
            _worker = null;
            _model = null;
        }
    }
}
