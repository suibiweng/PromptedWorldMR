// NegotiationUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NegotiationUI : MonoBehaviour
{
    [Header("Panel")]
    public GameObject panelRoot;

    [Header("Fields")]
    public Image icon;
    public TMP_Text objectName;
    public TMP_Text objectSummary;
    public TMP_Dropdown quickFunctionDropdown;
    public TMP_InputField customFunctionInput;

    [Header("Freeform Context")]
    public TMP_InputField objectHintField;

    public Toggle addParticlesToggle;
    public Toggle addLuaToggle;

    [Header("Edit Mode")]
    [Tooltip("If ON, the LLM will receive previous Lua/particle JSON to continue editing.")]
    public Toggle continueEditingToggle;      // <-- NEW

    public TMP_Text affordanceWarning;
    public Button acceptButton;
    public Button cancelButton;

    [Header("Wiring")]
    public OpenAIProfileGenerator generator;
    public PromptedMatter targetMatter;

    private ObjectProfile _profile;

    void Awake()
    {
        if (panelRoot) panelRoot.SetActive(false);
        if (acceptButton) acceptButton.onClick.AddListener(OnAccept);
        if (cancelButton) cancelButton.onClick.AddListener(Close);
    }

    public void Open(PromptedMatter pm)
    {
        targetMatter = pm;
        _profile = pm ? pm.objectProfile : null;

        if (panelRoot) panelRoot.SetActive(true);

        if (icon) icon.sprite = _profile && _profile.icon ? _profile.icon : null;
        if (objectName) objectName.text = _profile ? _profile.displayName : "Object";
        if (objectSummary) objectSummary.text = _profile ? _profile.description : "Provide a short hint below if needed.";

        if (quickFunctionDropdown)
        {
            quickFunctionDropdown.ClearOptions();
            if (_profile && _profile.defaultFunctions != null && _profile.defaultFunctions.Length > 0)
                quickFunctionDropdown.AddOptions(new System.Collections.Generic.List<string>(_profile.defaultFunctions));
            else
                quickFunctionDropdown.AddOptions(new System.Collections.Generic.List<string> { "(none)" });
        }

        if (objectHintField) objectHintField.text = pm ? pm.objectHint : "";

        if (affordanceWarning) affordanceWarning.text = "";
        if (addParticlesToggle) addParticlesToggle.isOn = true;
        if (addLuaToggle) addLuaToggle.isOn = true;

        if (continueEditingToggle) continueEditingToggle.isOn = true; // default ON
    }

    public void Close()
    {
        if (panelRoot) panelRoot.SetActive(false);
    }

    private void OnAccept()
    {
        if (generator == null || targetMatter == null)
        {
            Debug.LogError("[NegotiationUI] Missing generator or target.");
            Close();
            return;
        }

        if (objectHintField && targetMatter)
            targetMatter.objectHint = objectHintField.text;

        string quick = quickFunctionDropdown ? quickFunctionDropdown.captionText.text : "";
        string custom = customFunctionInput ? customFunctionInput.text : "";
        string chosen = string.IsNullOrWhiteSpace(custom) ? quick : custom;

        if (_profile != null && affordanceWarning)
        {
            if (chosen.ToLower().Contains("pour") && !_profile.affordances.HasFlag(AffordanceFlags.Pourable))
                affordanceWarning.text = "Warning: action suggests 'Pourable' but profile lacks that affordance.";
            else
                affordanceWarning.text = "";
        }

        string particleHint = addParticlesToggle && addParticlesToggle.isOn
            ? "If suitable, include particle_json."
            : "Do NOT include particle_json.";
        string luaHint = addLuaToggle && addLuaToggle.isOn
            ? "Include minimal safe lua_code if needed."
            : "Set lua_code to empty string.";

        string userPrompt = $"Behavior request: '{chosen}'. {particleHint} {luaHint}";

        generator.targetMatter = targetMatter;
        generator.applyParticles = addParticlesToggle ? addParticlesToggle.isOn : true;
        generator.applyLua = addLuaToggle ? addLuaToggle.isOn : true;

        // ---------- NEW: wire the toggle ----------
        generator.continueEditing = continueEditingToggle ? continueEditingToggle.isOn : true;

        generator.GenerateFromUserPrompt(userPrompt);
        Close();
    }
}
