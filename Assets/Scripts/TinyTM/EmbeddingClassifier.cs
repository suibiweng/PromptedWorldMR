using System.Collections.Generic;
using UnityEngine;

namespace TinyTM
{
    public class EmbeddingClassifier
    {
        public class Sample { public string label; public float[] vec; }
        private readonly List<Sample> _samples = new List<Sample>();
        private readonly int _k;
        public int Count => _samples.Count;

        public EmbeddingClassifier(int k = 3) { _k = Mathf.Max(1, k); }
        public void Clear() => _samples.Clear();
        public List<Sample> Snapshot() => new List<Sample>(_samples);

        public void AddSample(string label, float[] embedding)
        {
            _samples.Add(new Sample { label = label, vec = Normalize(embedding) });
        }

        public string Predict(float[] embedding, out float topSim)
        {
            topSim = 0f;
            if (_samples.Count == 0) return null;
            var q = Normalize(embedding);

            string bestLabel = null; float best = -1f;
            for (int i = 0; i < _samples.Count; i++)
            {
                float sim = Cosine(q, _samples[i].vec);
                if (sim > best) { best = sim; bestLabel = _samples[i].label; }
            }
            topSim = best;
            return bestLabel;
        }

        static float Cosine(float[] a, float[] b)
        {
            float dot = 0f, na = 0f, nb = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i];
            }
            return dot / (Mathf.Sqrt(na) * Mathf.Sqrt(nb) + 1e-6f);
        }
        static float[] Normalize(float[] v)
        {
            float n = 0f; for (int i = 0; i < v.Length; i++) n += v[i] * v[i];
            n = Mathf.Sqrt(n) + 1e-6f;
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = v[i] / n;
            return o;
        }
    }
}
