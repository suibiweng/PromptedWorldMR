using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public class AgentsDirectory : MonoBehaviour
{
    private static AgentsDirectory _instance;
    public static AgentsDirectory Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("AgentsDirectory");
                _instance = go.AddComponent<AgentsDirectory>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    // Notifies UIs to refresh when the list changes
    public static event Action OnChanged;

    private readonly List<MatterDialogueAgent> _agents = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { _instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() { _ = Instance; } // force create early

    private void Awake()
    {
        // If user placed one in the scene, make it the singleton
        if (_instance == null) { _instance = this; DontDestroyOnLoad(gameObject); }
        else if (_instance != this) { Destroy(gameObject); return; }

        // Seed once from current scene (agents that are already active)
        SeedFromScene();
    }

    public static void SeedFromScene()
    {
        var all = FindObjectsOfType<MatterDialogueAgent>(true);
        foreach (var a in all)
        {
            // Only track enabled/active agents; they’ll re-register on enable
            if (a.isActiveAndEnabled) Register(a);
        }
        OnChanged?.Invoke();
    }

    public static void Register(MatterDialogueAgent a)
    {
        if (!a) return;
        var dir = Instance;
        if (!dir._agents.Contains(a))
        {
            dir._agents.Add(a);
            OnChanged?.Invoke();
        }
    }

    public static void Unregister(MatterDialogueAgent a)
    {
        if (!a) return;
        var dir = Instance;
        if (dir._agents.Remove(a))
        {
            OnChanged?.Invoke();
        }
    }

    public static IReadOnlyList<MatterDialogueAgent> All => Instance._agents;
}
