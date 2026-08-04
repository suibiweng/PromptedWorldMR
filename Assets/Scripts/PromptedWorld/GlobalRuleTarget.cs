using UnityEngine;

[DisallowMultipleComponent]
public class GlobalRuleTarget : MonoBehaviour
{
    [Tooltip("When selected for prompting, treat this object as a room/environment controller instead of an ordinary object.")]
    public bool treatPromptsAsGlobalRules = true;

    [Tooltip("Short role name shown to the Lua generator.")]
    public string roleName = "EnvironmentController";

    [TextArea(2, 5)]
    [Tooltip("Extra instruction appended to generation context for this global controller.")]
    public string generationHint =
        "This object is a global environment controller. Generate room-level rules here. Do not assume 'this object' means a physical furniture target unless the user says so.";
}
