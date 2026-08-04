using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptedWorld;

public class LuaPromptUI : MonoBehaviour
{
    [Header("Generator")]
    [SerializeField] private OpenAILuaGenerator generator;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField objectNameInput;
    [SerializeField] private TMP_InputField promptInput;
    [SerializeField] private TMP_Dropdown modelDropdown;

    [Header("Options")]
    [SerializeField] private Toggle autoApplyToggle;
    [Tooltip("If on, generated Lua starts playing immediately after it is applied. If off, Lua is assigned but stopped.")]
    [SerializeField] private Toggle playOnGenerateToggle;
    [Tooltip("Legacy alias for Play On Generate. Prefer playOnGenerateToggle.")]
    [SerializeField] private Toggle callStartToggle;
    [SerializeField] private Toggle applyToAllSelectedToggle;

    [Header("Buttons")]
    [SerializeField] private Button selectTargetButton;
    [SerializeField] private Button startButton;

    [Header("Play / Stop Lua")]
    [SerializeField] private Button playLuaButton;
    [SerializeField] private Button stopLuaButton;
    [SerializeField] private Button deleteObjectButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text selectedObjectNameText;

    [Header("Selection")]
    [SerializeField] private GameObject currentTarget;
    private bool explicitTargetPicked;

    [Header("World References")]
    public PromptedWorldManager pwm;

    [Header("Group / Lasso (optional)")]
    [Tooltip("If assigned, the lasso's current selection will be used as part of the group when generating Lua.")]
    public LassoSelectorMR3D lassoSelector;

    private void Awake()
    {
        if (!pwm)           pwm           = FindObjectOfType<PromptedWorldManager>();
        if (!generator)     generator     = FindObjectOfType<OpenAILuaGenerator>();
        if (!lassoSelector) lassoSelector = FindObjectOfType<LassoSelectorMR3D>();

        if (selectTargetButton)
            selectTargetButton.onClick.AddListener(BeginSelectTarget);
        if (startButton)
            startButton.onClick.AddListener(StartGeneration);

        if (playLuaButton)
            playLuaButton.onClick.AddListener(PlaySelectedLua);
        if (stopLuaButton)
            stopLuaButton.onClick.AddListener(StopSelectedLua);
        if (deleteObjectButton)
            deleteObjectButton.onClick.AddListener(DeleteSelectedVirtualObjects);

        InitializePlayOnGenerateToggle();
        if (playOnGenerateToggle)
            playOnGenerateToggle.onValueChanged.AddListener(SetPlayOnGenerate);
        UpdateSelectedObjectNameDisplay(currentTarget);
        UpdateStatus("Select one or more objects (click or lasso), then press Start.");

        Debug.Log($"[LuaPromptUI] Awake. pwm={pwm}, generator={generator}, lassoSelector={lassoSelector}");
    }

    /// <summary>
    /// Called by RaycastTargetPicker after user clicks an object.
    /// </summary>
    public void OnPickedTarget(GameObject go)
    {
        if (go == null)
        {
            UpdateStatus("Picked target is null.");
            return;
        }

        var programableObject = go.GetComponentInParent<ProgramableObject>();
        if (programableObject != null)
            go = programableObject.gameObject;

        SetTarget(go);
        explicitTargetPicked = true;

        if (pwm != null)
        {
            pwm.SetPrimarySelectedObject(go);
        }
    }

    private void SetTarget(GameObject go)
    {
        currentTarget = go;

        if (currentTarget && generator)
        {
            generator.AssignTarget(currentTarget);

            if (objectNameInput)
                objectNameInput.text = currentTarget.name;

            UpdateSelectedObjectNameDisplay(currentTarget);
            UpdateStatus($"Target selected: {currentTarget.name}. Now press Start.");
        }
        else
        {
            UpdateSelectedObjectNameDisplay(null);
            UpdateStatus("No target selected.");
        }
    }

    private void StartGeneration()
    {
        if (!generator)
        {
            UpdateStatus("Generator missing in scene.");
            return;
        }

        // Guard: make sure we have some prompt text
        if (promptInput == null || string.IsNullOrWhiteSpace(promptInput.text))
        {
            UpdateStatus("Type a prompt before generating.");
            return;
        }

        // Build a combined selection from:
        // - Lasso
        // - PromptedWorldManager's dynamic selectedObjects list
        List<GameObject> group = new List<GameObject>();
        HashSet<GameObject> seen = new HashSet<GameObject>();
        GameObject mainTarget = null;
        GameObject firstLassoTarget = null;
        GameObject lastClickTarget = null;

        // 1) Lasso selection
        if (lassoSelector != null)
        {
            var selection = lassoSelector.GetCurrentSelection();
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    var go = selection[i];
                    if (go != null && seen.Add(go))
                    {
                        group.Add(go);
                        if (firstLassoTarget == null)
                            firstLassoTarget = go;
                    }
                }
            }
        }

        // 2) Click selection (dynamic list on PromptedWorldManager)
        if (pwm != null)
        {
            var clickSel = pwm.GetSelectedObjects();
            if (clickSel != null)
            {
                for (int i = 0; i < clickSel.Count; i++)
                {
                    var go = clickSel[i];
                    if (go != null && seen.Add(go))
                    {
                        group.Add(go);
                    }

                    if (go != null)
                        lastClickTarget = go;
                }
            }
        }

        // 3) Primary target priority:
        // explicit picked target, latest clicked PWM target, last click-selection item,
        // then first lasso object as a final fallback.
        if (explicitTargetPicked && currentTarget != null)
        {
            mainTarget = currentTarget;
            if (seen.Add(mainTarget))
                group.Add(mainTarget);
        }

        if (mainTarget == null && pwm != null && pwm.selectedObject != null &&
            (group.Count == 0 || group.Contains(pwm.selectedObject)))
        {
            mainTarget = pwm.selectedObject;
            if (!seen.Contains(mainTarget))
                group.Add(mainTarget);
        }

        if (mainTarget == null && lastClickTarget != null)
        {
            mainTarget = lastClickTarget;
        }

        if (mainTarget == null && firstLassoTarget != null)
        {
            mainTarget = firstLassoTarget;
        }

        // 4) If still nothing, try whatever the UI last picked
        if (mainTarget == null && currentTarget != null)
        {
            mainTarget = currentTarget;
            if (!seen.Contains(mainTarget))
                group.Add(mainTarget);
        }

        // 5) Last-resort fallback: look for any ProgramableObject with latched highlight
        if (mainTarget == null)
        {
            var allPO = FindObjectsOfType<ProgramableObject>();
            ProgramableObject latched = null;

            foreach (var po in allPO)
            {
                if (po == null) continue;
                // if you changed the field name, adjust here
                if (po.highlightLatched)
                {
                    latched = po;
                    break;
                }
            }

            if (latched != null)
            {
                mainTarget = latched.gameObject;
                if (!seen.Contains(mainTarget))
                    group.Add(mainTarget);

                Debug.Log("[LuaPromptUI] Fallback: using latched ProgramableObject " + mainTarget.name);
            }
        }

        // 6) If we still have nothing, then really nothing is selected
        if (mainTarget == null)
        {
            UpdateStatus("Select at least one object (click or lasso) first.");
            generator.EnableGroupBroadcast(false);
            generator.SetGroupTargets(null);
            Debug.LogWarning("[LuaPromptUI] StartGeneration: no mainTarget found after all fallbacks.");
            return;
        }

        currentTarget = mainTarget;
        UpdateSelectedObjectNameDisplay(currentTarget);

        Debug.Log($"[LuaPromptUI] StartGeneration: mainTarget={currentTarget.name}, groupCount={group.Count}");

        UpdateStatus("Current target: " + currentTarget.name);

        // Keep PWM single-selection in sync for legacy flows
        if (pwm != null && pwm.selectedObject != currentTarget)
        {
            pwm.SetPrimarySelectedObject(currentTarget);
        }

        // Push UI values into generator
        generator.naturalLanguageIntent = promptInput.text.Trim();

        if (objectNameInput)
            generator.objectDisplayName = objectNameInput.text;

        if (modelDropdown && modelDropdown.options.Count > 0)
        {
            var field = typeof(OpenAILuaGenerator).GetField(
                "model",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public
            );
            if (field != null)
            {
                field.SetValue(generator, modelDropdown.options[modelDropdown.value].text);
            }
        }

        if (autoApplyToggle)
            generator.autoApplyToLuaBehaviour = autoApplyToggle.isOn;

        generator.callStartAfterApply = GetPlayOnGenerateEnabled();

        // 7) Configure group broadcast only when explicitly requested.
        bool applyToAllSelected = applyToAllSelectedToggle
            ? applyToAllSelectedToggle.isOn
            : IntentRequestsGroupBroadcast(promptInput.text);

        if (group.Count > 1 && applyToAllSelected)
        {
            Debug.Log("[LuaPromptUI] Broadcasting Lua to group of " + group.Count + " objects.");
            generator.EnableGroupBroadcast(true);
            generator.SetGroupTargets(group);
        }
        else
        {
            // Single-object case
            generator.EnableGroupBroadcast(false);
            generator.SetGroupTargets(null);
        }

        // Ensure target is set
        generator.AssignTarget(currentTarget);
        generator.SetSelectedContext(currentTarget, group);

        UpdateStatus("Generating...");
        generator.GenerateLuaNow();

        // 8) Optional: break the temporary lasso group parent after generation
        if (lassoSelector != null)
        {
            lassoSelector.BreakCurrentGroup();
        }
    }

    private void InitializePlayOnGenerateToggle()
    {
        Toggle toggle = playOnGenerateToggle != null ? playOnGenerateToggle : callStartToggle;
        if (toggle == null || generator == null)
            return;

        toggle.isOn = generator.callStartAfterApply;

        if (playOnGenerateToggle != null && callStartToggle != null && callStartToggle != playOnGenerateToggle)
            callStartToggle.isOn = playOnGenerateToggle.isOn;
    }

    public void SetPlayOnGenerate(bool enabled)
    {
        if (generator != null)
            generator.callStartAfterApply = enabled;

        if (playOnGenerateToggle != null && playOnGenerateToggle.isOn != enabled)
            playOnGenerateToggle.isOn = enabled;

        if (callStartToggle != null && callStartToggle.isOn != enabled)
            callStartToggle.isOn = enabled;
    }

    private bool GetPlayOnGenerateEnabled()
    {
        if (playOnGenerateToggle != null)
        {
            if (callStartToggle != null && callStartToggle != playOnGenerateToggle)
                callStartToggle.isOn = playOnGenerateToggle.isOn;
            return playOnGenerateToggle.isOn;
        }

        return callStartToggle == null || callStartToggle.isOn;
    }

    /// <summary>
    /// Build current selection (same rules as StartGeneration) for Play/Stop.
    /// Returns true if at least one object is found.
    /// </summary>
    private bool TryBuildSelection(out GameObject mainTarget, out List<GameObject> group)
    {
        group = new List<GameObject>();
        HashSet<GameObject> seen = new HashSet<GameObject>();
        mainTarget = null;

        // 1) Lasso selection
        if (lassoSelector != null)
        {
            var selection = lassoSelector.GetCurrentSelection();
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    var go = selection[i];
                    if (go != null && seen.Add(go))
                    {
                        group.Add(go);
                    }
                }
            }
        }

        // 2) Click selection (dynamic list on PromptedWorldManager)
        if (pwm != null)
        {
            var clickSel = pwm.GetSelectedObjects();
            if (clickSel != null)
            {
                for (int i = 0; i < clickSel.Count; i++)
                {
                    var go = clickSel[i];
                    if (go != null && seen.Add(go))
                    {
                        group.Add(go);
                    }
                }
            }
        }

        // Decide mainTarget from group, if any
        if (group.Count > 0)
        {
            mainTarget = group[0];
        }

        // 3) If no group, try PromptedWorldManager.selectedObject
        if (mainTarget == null && pwm != null && pwm.selectedObject != null)
        {
            mainTarget = pwm.selectedObject;
            if (!seen.Contains(mainTarget))
                group.Add(mainTarget);
        }

        // 4) If still nothing, try whatever the UI last picked
        if (mainTarget == null && currentTarget != null)
        {
            mainTarget = currentTarget;
            if (!seen.Contains(mainTarget))
                group.Add(mainTarget);
        }

        // 5) Last-resort fallback: look for any ProgramableObject with latched highlight
        if (mainTarget == null)
        {
            var allPO = FindObjectsOfType<ProgramableObject>();
            ProgramableObject latched = null;

            foreach (var po in allPO)
            {
                if (po == null) continue;
                if (po.highlightLatched)
                {
                    latched = po;
                    break;
                }
            }

            if (latched != null)
            {
                mainTarget = latched.gameObject;
                if (!seen.Contains(mainTarget))
                    group.Add(mainTarget);

                Debug.Log("[LuaPromptUI] Play/Stop selection fallback: using latched ProgramableObject " + mainTarget.name);
            }
        }

        if (mainTarget == null)
        {
            return false;
        }

        currentTarget = mainTarget;
        UpdateSelectedObjectNameDisplay(currentTarget);
        return true;
    }

    private void PlaySelectedLua()
    {
        GameObject mainTarget;
        List<GameObject> group;

        if (!TryBuildSelection(out mainTarget, out group))
        {
            UpdateStatus("Select at least one object (click or lasso) before Play.");
            Debug.LogWarning("[LuaPromptUI] PlaySelectedLua: no selection.");
            return;
        }

        if (group.Count == 0 && mainTarget != null)
            group.Add(mainTarget);

        foreach (var go in group)
        {
            if (!go) continue;
            go.SendMessage("PlayLua", SendMessageOptions.DontRequireReceiver);
        }

        if (group.Count > 1)
            UpdateStatus($"Play Lua on group ({group.Count} objects).");
        else
            UpdateStatus($"Play Lua on {mainTarget.name}.");
    }

    private void StopSelectedLua()
    {
        GameObject mainTarget;
        List<GameObject> group;

        if (!TryBuildSelection(out mainTarget, out group))
        {
            UpdateStatus("Select at least one object (click or lasso) before Stop.");
            Debug.LogWarning("[LuaPromptUI] StopSelectedLua: no selection.");
            return;
        }

        if (group.Count == 0 && mainTarget != null)
            group.Add(mainTarget);

        foreach (var go in group)
        {
            if (!go) continue;
            go.SendMessage("StopLua", SendMessageOptions.DontRequireReceiver);
        }

        if (group.Count > 1)
            UpdateStatus($"Stop Lua on group ({group.Count} objects).");
        else
            UpdateStatus($"Stop Lua on {mainTarget.name}.");
    }

    private void DeleteSelectedVirtualObjects()
    {
        if (pwm == null)
        {
            UpdateStatus("PromptedWorldManager missing in scene.");
            return;
        }

        GameObject mainTarget;
        List<GameObject> group;

        if (!TryBuildSelection(out mainTarget, out group))
        {
            UpdateStatus("Select at least one virtual object before Delete.");
            Debug.LogWarning("[LuaPromptUI] DeleteSelectedVirtualObjects: no selection.");
            return;
        }

        if (group.Count == 0 && mainTarget != null)
            group.Add(mainTarget);

        int deleted = 0;
        foreach (var go in group)
        {
            if (pwm.DeleteVirtualObject(go))
                deleted++;
        }

        if (deleted == 0)
        {
            UpdateStatus("No virtual programmable objects deleted. Real objects and global controllers are protected.");
            return;
        }

        currentTarget = pwm.selectedObject;
        explicitTargetPicked = currentTarget != null;
        UpdateSelectedObjectNameDisplay(currentTarget);

        if (generator != null && currentTarget != null)
            generator.AssignTarget(currentTarget);

        UpdateStatus(deleted == 1
            ? "Deleted selected virtual object."
            : $"Deleted {deleted} selected virtual objects.");
    }

    private void BeginSelectTarget()
    {
        var picker = FindObjectOfType<RaycastTargetPicker>();
        if (!picker)
        {
            UpdateStatus("No RaycastTargetPicker found in scene.");
            return;
        }

        picker.BeginPick(this);
        UpdateStatus("Click an object to select it…");
    }

    private void UpdateStatus(string msg)
    {
        if (statusText)
            statusText.text = msg;
        else
            Debug.Log("[LuaPromptUI] " + msg);
    }

    private void UpdateSelectedObjectNameDisplay(GameObject target)
    {
        if (!selectedObjectNameText)
            return;

        selectedObjectNameText.text = target
            ? $"Selected: {GetDisplayName(target)}"
            : "Selected: (none)";
    }

    private string GetDisplayName(GameObject target)
    {
        if (!target)
            return "(none)";

        var programableObject = target.GetComponentInParent<ProgramableObject>();
        if (programableObject != null && programableObject.TextBox != null && !string.IsNullOrWhiteSpace(programableObject.TextBox.text))
            return $"{programableObject.TextBox.text.Trim()} ({target.name})";

        return target.name;
    }

    private bool IntentRequestsGroupBroadcast(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return false;

        string normalized = " " + intent.Trim().ToLowerInvariant() + " ";
        return normalized.Contains(" all ") ||
               normalized.Contains(" every ") ||
               normalized.Contains(" each ") ||
               normalized.Contains(" both ") ||
               normalized.Contains(" selected objects ") ||
               normalized.Contains(" entire group ");
    }
}
