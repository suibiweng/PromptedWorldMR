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
    [SerializeField] private Toggle callStartToggle;

    [Header("Buttons")]
    [SerializeField] private Button selectTargetButton;
    [SerializeField] private Button startButton;

    [Header("Play / Stop Lua")]
    [SerializeField] private Button playLuaButton;
    [SerializeField] private Button stopLuaButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;

    [Header("Selection")]
    [SerializeField] private GameObject currentTarget;

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

        SetTarget(go);

        if (pwm != null)
        {
            // Legacy single-selection path
            pwm.setSelectedObject(go);
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

            UpdateStatus($"Target selected: {currentTarget.name}. Now press Start.");
        }
        else
        {
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

        Debug.Log($"[LuaPromptUI] StartGeneration: mainTarget={currentTarget.name}, groupCount={group.Count}");

        UpdateStatus("Current target: " + currentTarget.name);

        // Keep PWM single-selection in sync for legacy flows
        if (pwm != null && pwm.selectedObject != currentTarget)
        {
            pwm.setSelectedObject(currentTarget);
        }

        // Real-object tag for the prompt
        string objectTag = "";
        var progObj = currentTarget.GetComponent<ProgramableObject>();
        if (progObj != null && progObj.isRealObject)
        {
            objectTag = "[This is a realobject] ";
        }

        // Push UI values into generator
        generator.naturalLanguageIntent = objectTag + promptInput.text;

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

        if (callStartToggle)
            generator.callStartAfterApply = callStartToggle.isOn;

        // 7) Configure group broadcast based on how many objects are selected
        if (group.Count > 1)
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

        UpdateStatus("Generating...");
        generator.GenerateLuaNow();

        // 8) Optional: break the temporary lasso group parent after generation
        if (lassoSelector != null)
        {
            lassoSelector.BreakCurrentGroup();
        }
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
}
