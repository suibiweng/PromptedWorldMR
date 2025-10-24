using UnityEngine;

public class ExampleButtonHook : MonoBehaviour
{
    public OpenAIProfileGenerator generator;

    public void OnGenerateClicked(string promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText))
            promptText = "Make it gently steam; no Lua changes.";
        generator.GenerateFromUserPrompt(promptText);
    }
}
