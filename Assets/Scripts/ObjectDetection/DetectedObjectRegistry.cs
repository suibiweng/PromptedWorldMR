using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

public class DetectedObjectRegistry : MonoBehaviour
{
    public static DetectedObjectRegistry Instance;

    public List<DetectedObjectInfo> objects = new List<DetectedObjectInfo>();

    [Header("UI Output")]
    public TMP_Text outputText;

    // label -> counter (Desktop -> 3)
    private Dictionary<string, int> labelCounters = new Dictionary<string, int>();

    // Transform -> DetectedObjectInfo (stable identity)
    private Dictionary<Transform, DetectedObjectInfo> objectMap = new Dictionary<Transform, DetectedObjectInfo>();

    void Awake()
    {
        Instance = this;
    }

    // 🔥 Call this from StableObjectTrackerFromAgent
    public void RegisterOrUpdate(string label, Transform objTransform, Renderer renderer)
    {
        if (objTransform == null) return;

        if (objectMap.TryGetValue(objTransform, out var info))
        {
            // Update existing
            info.position = objTransform.position;
            info.rotationEuler = objTransform.rotation.eulerAngles;
            if (renderer != null)
                info.size = renderer.bounds.size;
        }
        else
        {
            // Create new
            if (!labelCounters.ContainsKey(label))
                labelCounters[label] = 1;
            else
                labelCounters[label]++;

            int index = labelCounters[label];

            info = new DetectedObjectInfo();
            info.label = label;
            info.uniqueName = label + index;
            info.position = objTransform.position;
            info.rotationEuler = objTransform.rotation.eulerAngles;

            if (renderer != null)
                info.size = renderer.bounds.size;

            objects.Add(info);
            objectMap[objTransform] = info;
        }

        UpdateTMP();
    }

    public void Remove(Transform objTransform)
    {
        if (objTransform == null) return;

        if (objectMap.TryGetValue(objTransform, out var info))
        {
            objects.Remove(info);
            objectMap.Remove(objTransform);
            UpdateTMP();
        }
    }

    void UpdateTMP()
    {
        if (outputText == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== OBJECT REGISTRY ===\n");

        foreach (var obj in objects)
        {
            sb.AppendLine(obj.uniqueName);
            sb.AppendLine($"  Label: {obj.label}");
            sb.AppendLine($"  Pos: {obj.position}");
            sb.AppendLine($"  Rot: {obj.rotationEuler}");
            sb.AppendLine($"  Size: {obj.size}");
            sb.AppendLine();
        }

        outputText.text = sb.ToString();
    }
}
