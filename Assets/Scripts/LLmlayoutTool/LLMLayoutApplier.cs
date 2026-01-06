using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class LLMLayoutApplier : MonoBehaviour
{
    [Header("Overrides")]
    public Transform[] existingObjects;

    // 🔒 REQUIRED for LayoutRunner compatibility
    public GameObject sourceObject;

    [Header("Debug")]
    public bool verboseLogging = true;

    private string pendingJson;
    public bool HasNewLayoutJson => !string.IsNullOrEmpty(pendingJson);

    // =====================================================
    // PUBLIC API
    // =====================================================

    public void SetLayoutJson(string json)
    {
        pendingJson = json;
    }

    public string ConsumeLayoutJson()
    {
        string j = pendingJson;
        pendingJson = null;
        return j;
    }

    // =====================================================
    // APPLY LAYOUT (EXACT ID MATCH ONLY)
    // =====================================================

    public void ApplyLayoutFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        LayoutJson layout = JsonUtility.FromJson<LayoutJson>(json);
        if (layout == null || layout.instances == null || layout.instances.Length == 0)
            return;

        if (existingObjects == null || existingObjects.Length == 0)
        {
            Debug.LogWarning("[LLMLayoutApplier] No existingObjects assigned.");
            return;
        }

        // 🔒 Build exact ID → instance map
        Dictionary<string, LayoutInstance> instanceMap = new Dictionary<string, LayoutInstance>();

        foreach (var inst in layout.instances)
        {
            if (string.IsNullOrEmpty(inst.id))
                continue;

            string key = inst.id.ToLowerInvariant();
            if (!instanceMap.ContainsKey(key))
                instanceMap.Add(key, inst);
        }

        if (verboseLogging)
        {
            Debug.Log(
                $"[LLMLayoutApplier] Applying layout (EXACT ID MATCH). instances={instanceMap.Count}, existingObjects={existingObjects.Length}"
            );
        }

        foreach (var t in existingObjects)
        {
            if (t == null) continue;

            var po = t.GetComponent<ProgramableObject>();
            if (po == null || string.IsNullOrEmpty(po.id))
            {
                if (verboseLogging)
                    Debug.LogWarning($"[LLMLayoutApplier] Missing ProgramableObject.id on '{t.name}'");
                continue;
            }

            string objId = po.id.ToLowerInvariant();

            if (!instanceMap.TryGetValue(objId, out var inst))
            {
                if (verboseLogging)
                    Debug.LogWarning($"[LLMLayoutApplier] No layout entry for id '{po.id}'");
                continue;
            }

            ApplyInstance(inst, t);
        }
    }

    // =====================================================
    // APPLY SINGLE INSTANCE
    // =====================================================

    private void ApplyInstance(LayoutInstance inst, Transform target)
    {
        if (inst == null || target == null)
            return;

        target.localPosition = inst.position.ToVector3();
        target.localRotation = Quaternion.Euler(inst.rotationEuler.ToVector3());
        target.localScale = inst.scale.ToVector3();
    }

    // =====================================================
    // JSON DTOs
    // =====================================================

    [System.Serializable]
    private class LayoutJson
    {
        public string layout_name;
        public string space;
        public LayoutInstance[] instances;
    }

    [System.Serializable]
    private class LayoutInstance
    {
        public string id;
        public Vec3 position;
        public Vec3 rotationEuler;
        public Vec3 scale;
    }

    [System.Serializable]
    private class Vec3
    {
        public float x, y, z;
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }
}
