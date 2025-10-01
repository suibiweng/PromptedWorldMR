// Assets/Scripts/LuaPromptUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LuaPromptUI : MonoBehaviour
{
    [Header("Generator")]
    [SerializeField] private OpenAILuaGenerator generator;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField objectNameInput;
    [SerializeField] private TMP_InputField promptInput;
    [SerializeField] private TMP_Dropdown modelDropdown; // optional (can be null)
    [SerializeField] private Toggle autoApplyToggle;     // optional
    [SerializeField] private Toggle callStartToggle;     // optional

    [Header("Buttons")]
    [SerializeField] private Button selectTargetButton;
    [SerializeField] private Button generateButton;

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;        // optional

    [Header("Target")]
    [SerializeField] private GameObject currentTarget;   // show in inspector

    private void Awake()
    {
        if (!generator)
        {
            generator = FindObjectOfType<OpenAILuaGenerator>();
        }

        if (selectTargetButton) selectTargetButton.onClick.AddListener(BeginSelectTarget);
        if (generateButton) generateButton.onClick.AddListener(Generate);
    }

    private void OnEnable()
    {
        UpdateStatus("Idle");
    }

    public void SetTarget(GameObject go)
    {
        currentTarget = go;
        if (generator) generator.AssignTarget(currentTarget);
        if (objectNameInput && currentTarget) objectNameInput.text = currentTarget.name;
        UpdateStatus(currentTarget ? $"Target: {currentTarget.name}" : "Target: none");
    }

    public void Generate()
    {
        if (!generator)
        {
            UpdateStatus("Generator missing");
            return;
        }

        // Apply UI options to generator
        if (autoApplyToggle) generator.autoApplyToLuaBehaviour = autoApplyToggle.isOn;
        if (callStartToggle) generator.callStartAfterApply = callStartToggle.isOn;

        if (modelDropdown && modelDropdown.options.Count > 0)
        {
            // Optional: map dropdown values to model names in the Inspector text
            // Example options: gpt-4o, gpt-4o-mini, gpt-4.1-mini
            generator.GetType().GetField("model", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                ?.SetValue(generator, modelDropdown.options[modelDropdown.value].text);
        }

        // Prompt and object name
        if (promptInput) generator.naturalLanguageIntent = promptInput.text;
        if (objectNameInput) generator.objectDisplayName = objectNameInput.text;

        // Ensure target is registered
        if (currentTarget && generator) generator.AssignTarget(currentTarget);

        UpdateStatus("Generating...");
        generator.GenerateLuaNow();
    }

    // Called by RaycastTargetPicker when a target is chosen
    public void OnPickedTarget(GameObject go)
    {
        SetTarget(go);
    }

    private void BeginSelectTarget()
    {
        var picker = FindObjectOfType<RaycastTargetPicker>();
        if (!picker)
        {
            UpdateStatus("No RaycastTargetPicker found in scene");
            return;
        }
        picker.BeginPick(this);
        UpdateStatus("Pick mode: click an object");
    }

    private void UpdateStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        else Debug.Log($"[LuaPromptUI] {msg}");
    }
}
