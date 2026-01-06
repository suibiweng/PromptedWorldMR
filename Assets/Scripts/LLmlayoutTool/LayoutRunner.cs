using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LayoutRunner : MonoBehaviour
{
    // ================================
    // PUBLIC API (DO NOT BREAK)
    // ================================

    [Header("Prompts")]
    [TextArea(2, 6)]
    public string initialLayoutPrompt;

    [TextArea(2, 6)]
    public string editLayoutPrompt;

    [Header("Overrides")]
    public Transform[] existingObjectsOverride;

    [Header("State")]
    public string currentLayoutName;

    public enum RunnerStatus
    {
        Idle,
        Requesting,
        Applied,
        Error
    }

    // 🔒 INTERNAL ENUM STATE
    [System.NonSerialized]
    public RunnerStatus _status = RunnerStatus.Idle;

    // 🔑 STRING-COMPATIBLE STATUS (USED BY UI / BRIDGES)
    public string status
    {
        get => _status.ToString();
        set
        {
            if (Enum.TryParse(value, true, out RunnerStatus parsed))
                _status = parsed;
            else
                Debug.LogWarning($"[LayoutRunner] Unknown status '{value}'");
        }
    }

    // ================================
    // DEPENDENCIES
    // ================================
    public LayoutLLMService layoutService;
    public LLMLayoutApplier applier;

    // ================================
    // INTERNAL
    // ================================
    private string pendingJson;
    private Transform[] activeObjects;

    // ================================
    // OLD API (USED BY UI / BRIDGE)
    // ================================

    public void GenerateLayout()
    {
        if (existingObjectsOverride == null || existingObjectsOverride.Length == 0)
        {
            Debug.LogWarning("[LayoutRunner] No objects provided");
            return;
        }

        GenerateLayoutForObjects(existingObjectsOverride, initialLayoutPrompt);
    }

    public void EditCurrentLayout()
    {
        if (existingObjectsOverride == null || existingObjectsOverride.Length == 0)
            return;

        GenerateLayoutForObjects(existingObjectsOverride, editLayoutPrompt);
    }

    // ================================
    // SAFE ENTRY POINT
    // ================================
    public void GenerateLayoutForObjects(Transform[] objects, string userPrompt)
    {
        if (layoutService == null || applier == null)
            return;

        if (objects == null || objects.Length == 0)
            return;

        activeObjects = objects;
        _status = RunnerStatus.Requesting;

        // 🔑 Authoritative IDs (ProgramableObject.id > name)
        List<string> ids = new List<string>();
        foreach (var t in objects)
        {
            var po = t.GetComponent<ProgramableObject>();
            ids.Add(po != null && !string.IsNullOrEmpty(po.id) ? po.id : t.name);
        }

        StartCoroutine(
            layoutService.RequestLayoutJson(
                userPrompt,
                ids,
                OnLayoutJson,
                OnLayoutError
            )
        );
    }

    private void OnLayoutJson(string json)
    {
        pendingJson = json;
    }

    private void OnLayoutError(string err)
    {
        Debug.LogError("[LayoutRunner] " + err);
        _status = RunnerStatus.Error;
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(pendingJson))
            return;

        applier.existingObjects = activeObjects;
        applier.ApplyLayoutFromJson(pendingJson);

        pendingJson = null;
        _status = RunnerStatus.Applied;

        Debug.Log("[LayoutRunner] Applied layout");
    }
}
