using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Applies layout JSON (from LLM) by spawning or relayouting objects.
/// - Treats `scale` as a MULTIPLIER, not an absolute value.
///   * In spawn mode: multiplier on prefab scale.
///   * In relayout mode: multiplier on current localScale.
/// - If there is exactly ONE existing object and sourceObject is null,
///   and the layout has multiple instances, that single object is used as
///   a template: it becomes instance 0, and we spawn clones for the rest.
/// </summary>
public class LLMLayoutApplier : MonoBehaviour
{
    [Header("References")]
    public LayoutLLMService layoutService;

    [Tooltip("Prefab or source object to spawn when creating new layouts. If null, and there is exactly one existing object, that object will be used as the template.")]
    public GameObject sourceObject;

    [Tooltip("Parent transform for local-space layouts (can be null for world-space).")]
    public Transform layoutParent;

    [Tooltip("Existing objects that the LLM may relayout (by id sequence).")]
    public Transform[] existingObjects;

    [Tooltip("Optional anchor that can be mentioned in prompts (e.g., 'around this object').")]
    public Transform anchorObject;

    [Header("Options")]
    [Tooltip("If true, logs detailed steps to the console.")]
    public bool verboseLogging = true;

    /// <summary>
    /// Last raw JSON layout applied (so UI/other tools can inspect).
    /// </summary>
    [TextArea(3, 20)]
    public string LastLayoutJson;

    // Internal representation of parsed layout
    [Serializable]
    public class LayoutVector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToUnityVector3() => new Vector3(x, y, z);
    }

    [Serializable]
    public class LayoutInstance
    {
        public string id;
        public LayoutVector3 position;
        public LayoutVector3 rotationEuler;
        public LayoutVector3 scale;
    }

    [Serializable]
    public class LayoutPlan
    {
        public string layout_name;
        public string space;
        public LayoutInstance[] instances;
    }

    private LayoutPlan _lastPlan;

    private void Reset()
    {
        if (layoutService == null)
            layoutService = FindObjectOfType<LayoutLLMService>();
    }

    // ---------------- PUBLIC API (called by LayoutRunner) ----------------

    public IEnumerator RequestAndApplyLayout(string prompt)
    {
        if (layoutService == null)
        {
            Debug.LogError("[LLMLayoutApplier] layoutService not assigned.");
            yield break;
        }

        if (verboseLogging)
        {
            Debug.Log("[LLMLayoutApplier] Sending layout prompt:\n" + prompt);
        }

        bool done = false;

        yield return layoutService.RequestLayoutJson(
            prompt,
            onJson: json =>
            {
                done = true;
                ApplyLayoutFromJson(json);
            },
            onError: err =>
            {
                done = true;
                Debug.LogError("[LLMLayoutApplier] LLM error: " + err);
            });

        if (!done)
        {
            Debug.LogError("[LLMLayoutApplier] RequestAndApplyLayout finished without callback.");
        }
    }

    public IEnumerator RequestAndApplyEditedLayout(string editPrompt)
    {
        // For now it's just the same call with a different prompt.
        yield return RequestAndApplyLayout(editPrompt);
    }

    // ---------------- CORE APPLY LOGIC ----------------

    public void ApplyLayoutFromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("[LLMLayoutApplier] Empty JSON string, cannot apply.");
            return;
        }

        LastLayoutJson = json;

        LayoutPlan plan;
        try
        {
            plan = JsonUtility.FromJson<LayoutPlan>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("[LLMLayoutApplier] Failed to parse layout JSON: " + e.Message + "\nJSON:\n" + json);
            return;
        }

        if (plan == null || plan.instances == null)
        {
            Debug.LogError("[LLMLayoutApplier] Parsed layout plan is null or has no instances.");
            return;
        }

        _lastPlan = plan;

        bool isLocal = string.Equals(plan.space, "local", StringComparison.OrdinalIgnoreCase);
        int instanceCount = plan.instances.Length;
        int existingCount = existingObjects != null ? existingObjects.Length : 0;

        // 1) Perfect relayout: same count → just move existing objects.
        bool canRelayoutExisting = existingCount > 0 && existingCount == instanceCount;

        // 2) Special case: exactly ONE existing object, no prefab, but multiple instances.
        bool singleExistingAsTemplate =
            !canRelayoutExisting &&
            existingCount == 1 &&
            sourceObject == null &&
            instanceCount > 1;

        if (singleExistingAsTemplate)
        {
            if (verboseLogging)
            {
                Debug.Log("[LLMLayoutApplier] Using single existing object as template for "
                          + instanceCount + " instances.");
            }

            UseSingleExistingAsTemplateAndSpawn(plan, isLocal);
            return;
        }

        if (canRelayoutExisting)
        {
            if (verboseLogging)
            {
                Debug.Log("[LLMLayoutApplier] Relayouting existing objects (count = " + existingCount + ").");
            }
            ApplyToExistingObjects(plan, isLocal);
        }
        else
        {
            if (sourceObject == null)
            {
                Debug.LogError("[LLMLayoutApplier] sourceObject is not assigned; cannot spawn copies.");
                return;
            }

            if (verboseLogging)
            {
                Debug.Log("[LLMLayoutApplier] Spawning copies of sourceObject; existingObjects count "
                          + existingCount + ", instances count " + instanceCount);
            }

            ApplyBySpawningCopies(plan, isLocal);
        }
    }

    /// <summary>
    /// Case: existingObjects.Length == 1, sourceObject == null, instances > 1.
    /// - Treat existingObjects[0] as the template.
    /// - Move it to match instances[0].
    /// - Spawn clones for instances[1..N-1].
    /// - Update existingObjects to include all of them for future relayout.
    /// </summary>
    private void UseSingleExistingAsTemplateAndSpawn(LayoutPlan plan, bool isLocal)
    {
        if (existingObjects == null || existingObjects.Length != 1 || existingObjects[0] == null)
        {
            Debug.LogError("[LLMLayoutApplier] UseSingleExistingAsTemplateAndSpawn called with invalid existingObjects.");
            return;
        }

        Transform template = existingObjects[0];
        GameObject templateGO = template.gameObject;

        // For future calls, we can treat this as our prefab too
        sourceObject = templateGO;

        LayoutInstance[] insts = plan.instances;
        int count = insts.Length;

        // We'll build a fresh list of all final objects (template + clones)
        Transform[] newExisting = new Transform[count];

        // 1) Apply layout to the template as instance 0 (relayout rules)
        {
            LayoutInstance inst0 = insts[0];

            Vector3 pos = inst0.position != null ? inst0.position.ToUnityVector3() : Vector3.zero;
            Vector3 euler = inst0.rotationEuler != null ? inst0.rotationEuler.ToUnityVector3() : Vector3.zero;

            Vector3 baseScale = template.localScale;
            Vector3 scaleMul = inst0.scale != null ? inst0.scale.ToUnityVector3() : Vector3.one;
            Vector3 finalScale = Vector3.Scale(baseScale, scaleMul);

            if (isLocal)
            {
                if (layoutParent != null && template.parent != layoutParent)
                {
                    template.SetParent(layoutParent, false);
                }

                template.localPosition = pos;
                template.localRotation = Quaternion.Euler(euler);
                template.localScale = finalScale;
            }
            else
            {
                template.position = pos;
                template.rotation = Quaternion.Euler(euler);
                template.localScale = finalScale;
            }

            newExisting[0] = template;
        }

        // 2) Spawn copies for the rest
        for (int i = 1; i < count; i++)
        {
            LayoutInstance inst = insts[i];
            GameObject clone = Instantiate(templateGO);

            Vector3 baseScale = templateGO.transform.localScale;
            Vector3 scaleMul = inst.scale != null ? inst.scale.ToUnityVector3() : Vector3.one;
            Vector3 finalScale = Vector3.Scale(baseScale, scaleMul);

            Vector3 pos = inst.position != null ? inst.position.ToUnityVector3() : Vector3.zero;
            Vector3 euler = inst.rotationEuler != null ? inst.rotationEuler.ToUnityVector3() : Vector3.zero;

            if (isLocal)
            {
                if (layoutParent != null)
                {
                    clone.transform.SetParent(layoutParent, false);
                    clone.transform.localPosition = pos;
                    clone.transform.localRotation = Quaternion.Euler(euler);
                }
                else
                {
                    clone.transform.position = pos;
                    clone.transform.rotation = Quaternion.Euler(euler);
                }

                clone.transform.localScale = finalScale;
            }
            else
            {
                clone.transform.position = pos;
                clone.transform.rotation = Quaternion.Euler(euler);
                clone.transform.localScale = finalScale;
            }

            newExisting[i] = clone.transform;
        }

        // Update existingObjects so future layouts can relayout these 3x3x3 cubes
        existingObjects = newExisting;

        if (verboseLogging)
        {
            Debug.Log("[LLMLayoutApplier] Built new existingObjects array of size " + existingObjects.Length +
                      " using single existing as template.");
        }
    }

    private void ApplyBySpawningCopies(LayoutPlan plan, bool isLocal)
    {
        if (sourceObject == null)
        {
            Debug.LogError("[LLMLayoutApplier] sourceObject is not assigned; cannot spawn copies.");
            return;
        }

        LayoutInstance[] insts = plan.instances;
        int count = insts.Length;

        // Optional: track all spawned ones as existingObjects for later edits
        Transform[] spawned = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            LayoutInstance inst = insts[i];
            GameObject clone = Instantiate(sourceObject);

            Vector3 baseScale = sourceObject.transform.localScale;
            Vector3 scaleMul = inst.scale != null ? inst.scale.ToUnityVector3() : Vector3.one;
            Vector3 finalScale = Vector3.Scale(baseScale, scaleMul);

            Vector3 pos = inst.position != null ? inst.position.ToUnityVector3() : Vector3.zero;
            Vector3 euler = inst.rotationEuler != null ? inst.rotationEuler.ToUnityVector3() : Vector3.zero;

            if (isLocal)
            {
                if (layoutParent != null)
                {
                    clone.transform.SetParent(layoutParent, false);
                    clone.transform.localPosition = pos;
                    clone.transform.localRotation = Quaternion.Euler(euler);
                }
                else
                {
                    clone.transform.position = pos;
                    clone.transform.rotation = Quaternion.Euler(euler);
                }

                clone.transform.localScale = finalScale;
            }
            else
            {
                clone.transform.position = pos;
                clone.transform.rotation = Quaternion.Euler(euler);
                clone.transform.localScale = finalScale;
            }

            spawned[i] = clone.transform;
        }

        // If you want to edit this layout later, keep them in existingObjects
        existingObjects = spawned;
    }

    private void ApplyToExistingObjects(LayoutPlan plan, bool isLocal)
    {
        if (existingObjects == null || existingObjects.Length == 0)
        {
            Debug.LogError("[LLMLayoutApplier] existingObjects is empty; cannot relayout.");
            return;
        }

        LayoutInstance[] insts = plan.instances;
        int count = Mathf.Min(existingObjects.Length, insts.Length);

        for (int i = 0; i < count; i++)
        {
            Transform t = existingObjects[i];
            LayoutInstance inst = insts[i];

            if (t == null) continue;

            Vector3 pos = inst.position != null ? inst.position.ToUnityVector3() : Vector3.zero;
            Vector3 euler = inst.rotationEuler != null ? inst.rotationEuler.ToUnityVector3() : Vector3.zero;

            // Compute final scale: CURRENT scale * LLM scale multiplier (default 1,1,1)
            Vector3 baseScale = t.localScale;
            Vector3 scaleMul = inst.scale != null ? inst.scale.ToUnityVector3() : Vector3.one;
            Vector3 finalScale = Vector3.Scale(baseScale, scaleMul);

            if (isLocal)
            {
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(euler);
                t.localScale = finalScale;
            }
            else
            {
                t.position = pos;
                t.rotation = Quaternion.Euler(euler);
                t.localScale = finalScale;
            }
        }
    }
}
