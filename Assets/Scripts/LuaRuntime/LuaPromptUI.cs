// Assets/Scripts/LuaPromptUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LuaPromptUI : MonoBehaviour
{
    [Header("Generator")]
    [SerializeField] private OpenAILuaGenerator generator;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField objectNameInput;  // optional: shows/edits display name
    [SerializeField] private TMP_InputField promptInput;      // required: your intent text
    [SerializeField] private TMP_Dropdown modelDropdown;      // optional

    [Header("Options")]
    [SerializeField] private Toggle autoApplyToggle;          // optional
    [SerializeField] private Toggle callStartToggle;          // optional

    [Header("Buttons")]
    [SerializeField] private Button selectTargetButton;
    [SerializeField] private Button startButton;              // <- press after selecting a target

    [Header("Status")]
    [SerializeField] private TMP_Text statusText;             // optional

    [Header("Selection")]
    [SerializeField] private GameObject currentTarget;        // inspector-visible

    private void Awake()
    {
        if (!generator) generator = FindObjectOfType<OpenAILuaGenerator>();
        if (selectTargetButton) selectTargetButton.onClick.AddListener(BeginSelectTarget);
        if (startButton)        startButton.onClick.AddListener(StartGeneration);
        UpdateStatus("Select a target, then press Start.");
    }

    // Called by RaycastTargetPicker after user clicks an object
    public void OnPickedTarget(GameObject go)
    {
        SetTarget(go);
    }

    private void SetTarget(GameObject go)
    {
        currentTarget = go;
        if (currentTarget && generator)
        {
            generator.AssignTarget(currentTarget);
            if (objectNameInput) objectNameInput.text = currentTarget.name;
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
        if (!currentTarget)
        {
            UpdateStatus("Pick a target first.");
            return;
        }

        // Apply UI -> generator
        if (promptInput)      generator.naturalLanguageIntent = promptInput.text;
        if (objectNameInput)  generator.objectDisplayName     = objectNameInput.text;

        if (modelDropdown && modelDropdown.options.Count > 0)
        {
            // set private "model" field via reflection so you can swap models from the dropdown
            var field = typeof(OpenAILuaGenerator).GetField("model",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null) field.SetValue(generator, modelDropdown.options[modelDropdown.value].text);
        }

        if (autoApplyToggle)  generator.autoApplyToLuaBehaviour  = autoApplyToggle.isOn;
        if (callStartToggle)  generator.callStartAfterApply      = callStartToggle.isOn;

        // Ensure target is still assigned (defensive)
        generator.AssignTarget(currentTarget);

        UpdateStatus("Generating...");
        generator.GenerateLuaNow();
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
        if (statusText) statusText.text = msg;
        else Debug.Log("[LuaPromptUI] " + msg);
    }
}
