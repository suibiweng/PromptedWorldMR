using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyTM
{
    [Serializable]
    public class TinyTMSampleDTO { public string label; public float[] vec; }

    [Serializable]
    public class TinyTMDatasetDTO { public List<TinyTMSampleDTO> samples = new(); }

    public static class TinyTMDataset
    {
        public static void Save(string filePath, List<EmbeddingClassifier.Sample> samples)
        {
            var dto = new TinyTMDatasetDTO();
            foreach (var s in samples)
                dto.samples.Add(new TinyTMSampleDTO { label = s.label, vec = s.vec });
            var json = JsonUtility.ToJson(dto);
            System.IO.File.WriteAllText(filePath, json);
        }

        public static List<EmbeddingClassifier.Sample> Load(string filePath)
        {
            if (!System.IO.File.Exists(filePath)) return new List<EmbeddingClassifier.Sample>();
            var json = System.IO.File.ReadAllText(filePath);
            var dto = JsonUtility.FromJson<TinyTMDatasetDTO>(json);
            var list = new List<EmbeddingClassifier.Sample>(dto.samples.Count);
            foreach (var s in dto.samples)
                list.Add(new EmbeddingClassifier.Sample { label = s.label, vec = s.vec });
            return list;
        }
    }
}
