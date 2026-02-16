using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Meta.XR.MRUtilityKit;   // Only for GetComponent<MRUKAnchor>()

public class ObjectManager : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text outputText;     // <-- Assign this in Inspector

    [Header("Debug (Read Only)")]
    [TextArea(10, 30)]
    public string debugOutput;      // <-- You can SEE this in Inspector

    [Header("EffectMesh Scan")]
    public bool scanUntilFound = true;
    public float scanInterval = 1.0f;

    private float _nextScanTime = 0f;
    private bool effectMeshInitialized = false;

    // ===============================
    // DATA STRUCTURE
    // ===============================
    [System.Serializable]
    public class WorldObject
    {
        public string label;       // "WALL_FACE", "TABLE", "laptop"
        public string uniqueName;  // "WALL_FACE1", "laptop2"
        public Transform transform;
        public Vector3 position;
        public Vector3 rotationEuler;
        public Vector3 scale;
    }

    public List<WorldObject> objects = new List<WorldObject>();

    // Identity by Transform
    private Dictionary<Transform, WorldObject> objectMap = new Dictionary<Transform, WorldObject>();
    private Dictionary<string, int> labelCounters = new Dictionary<string, int>();

    // ===============================
    // UNITY
    // ===============================
    void Start()
    {
        // Try immediately
        TryScanEffectMesh();
        UpdateText();
    }

    void Update()
    {
        // Only scan until EffectMesh is found
        if (scanUntilFound && !effectMeshInitialized)
        {
            if (Time.time > _nextScanTime)
            {
                _nextScanTime = Time.time + scanInterval;
                TryScanEffectMesh();
            }
        }

        // Always update transforms (trackers move!)
        UpdateAllTransforms();
        UpdateText();
    }

    // ===============================
    // PUBLIC API (FOR TRACKER)
    // ===============================
    public void RegisterOrUpdate(string label, Transform t)
    {
        if (t == null) return;

        if (!objectMap.TryGetValue(t, out var obj))
        {
            string uniqueName = MakeUniqueName(label);

            obj = new WorldObject()
            {
                label = label,
                uniqueName = uniqueName,
                transform = t
            };

            objectMap[t] = obj;
            objects.Add(obj);
        }

        obj.position = t.position;
        obj.rotationEuler = t.rotation.eulerAngles;
        obj.scale = t.localScale;
    }

    // ===============================
    // EFFECTMESH SCAN (BY MRUKAnchor PRESENCE ONLY)
    // ===============================
    void TryScanEffectMesh()
    {
        var allTransforms = FindObjectsOfType<Transform>(true);

        bool foundAny = false;

        foreach (var t in allTransforms)
        {
            if (t == null) continue;

            // Only objects that HAVE MRUKAnchor
            var anchor = t.GetComponent<MRUKAnchor>();
            if (anchor == null)
                continue;

            foundAny = true;

            // Use GameObject name ONLY
            string rawName = t.gameObject.name;
            if (string.IsNullOrEmpty(rawName))
                continue;

            // Strip "(Clone)" or "(1)"
            string name = rawName;
            int idx = name.IndexOf("(");
            if (idx >= 0)
                name = name.Substring(0, idx).Trim();

            string label = name.ToUpper();

            RegisterOrUpdate(label, t);
        }

        if (foundAny)
        {
            effectMeshInitialized = true;
            Debug.Log("ObjectManager: EffectMesh found and locked.");
        }
    }

    // ===============================
    // INTERNAL
    // ===============================
    void UpdateAllTransforms()
    {
        foreach (var obj in objects)
        {
            if (obj.transform == null) continue;

            obj.position = obj.transform.position;
            obj.rotationEuler = obj.transform.rotation.eulerAngles;
            obj.scale = obj.transform.localScale;
        }
    }

    string MakeUniqueName(string label)
    {
        if (!labelCounters.ContainsKey(label))
            labelCounters[label] = 0;

        labelCounters[label]++;
        return label + labelCounters[label];
    }

    void UpdateText()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("=== WORLD OBJECTS ===");

        foreach (var obj in objects)
        {
            sb.AppendLine(obj.uniqueName);
            sb.AppendLine("  Label: " + obj.label);
            sb.AppendLine("  Pos: " + obj.position.ToString("F3"));
            sb.AppendLine("  Rot: " + obj.rotationEuler.ToString("F1"));
            sb.AppendLine("  Scale: " + obj.scale.ToString("F3"));
            sb.AppendLine();
        }

        string finalText = sb.ToString();

        // Show in TMP
        if (outputText != null)
            outputText.text = finalText;

        // Also show in Inspector
        debugOutput = finalText;
    }


    public void Remove(Transform t)
{
    if (t == null) return;
    if (objectMap.TryGetValue(t, out var obj))
    {
        objects.Remove(obj);
        objectMap.Remove(t);
    }
}

}
