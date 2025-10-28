// Assets/Scripts/PromptedMatters/MatterDialogueAgent.cs
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class MatterDialogueAgent : MonoBehaviour
{
    [Header("Links")]
    public PromptedMattersManager manager;
    public PromptedMatter matter;

    [Header("Options")]
    public bool forwardPreviousState = true;
    public bool applyLua = true;
    public bool applyParticles = true;

    [Header("Per-Object UI (optional)")]
    [Tooltip("Assign the object’s local dialogue Canvas root here so the Central UI can show/hide it.")]
    public GameObject objectUIRoot;

    // Events for UIs
    public event Action<string> OnAssistantUtter;
    public event Action<string> OnStatus;
    public event Action<string> OnError;
    public event Action<bool, string> OnFinished;

    // Session state (needed by CentralPromptUI)
    private bool _sessionActive;

    private void Awake()
    {
        if (!matter) matter = GetComponentInParent<PromptedMatter>();
    }

    private void OnEnable()  { AgentsDirectory.Register(this); }
    private void OnDisable() { AgentsDirectory.Unregister(this); }

    public void Begin(string seedIdea)
    {
        if (!manager) { RaiseError("No manager assigned."); return; }
        _sessionActive = true; // optimistic; manager will complete
        manager.BeginFor(this, seedIdea);
    }

    public void ContinueUser(string userReply)
    {
        if (!manager) { RaiseError("No manager assigned."); return; }
        // Auto-begin if a continue arrives before a begin
        if (!_sessionActive)
        {
            _sessionActive = true;
            manager.BeginFor(this, userReply);
            return;
        }
        manager.ContinueFor(this, userReply);
    }

    // Manager callbacks → raise events
    public void RaiseAssistantUtter(string s)      => OnAssistantUtter?.Invoke(s);
    public void RaiseStatus(string s)              => OnStatus?.Invoke(s);
    public void RaiseError(string s)               => OnError?.Invoke(s);
    public void RaiseFinished(bool ok, string txt) { _sessionActive = false; OnFinished?.Invoke(ok, txt); }

    // Central UI uses this to hide/show local panel
    public void SetObjectUIActive(bool active)
    {
        if (objectUIRoot) objectUIRoot.SetActive(active);
    }

    public string DisplayName =>
        (matter && !string.IsNullOrWhiteSpace(matter.objectHint)) ? matter.objectHint :
        (matter ? matter.name : gameObject.name);

    // Called by CentralPromptUI on target switch so Agree/Send can auto-begin
    public void ResetSessionFlag() => _sessionActive = false;
}
