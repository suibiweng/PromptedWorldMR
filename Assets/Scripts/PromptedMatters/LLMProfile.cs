// LLMProfile.cs
using UnityEngine;

[CreateAssetMenu(fileName = "LLMProfile", menuName = "PromptedMatters/LLM Profile")]
public class LLMProfile : ScriptableObject
{
    [Header("Provider / Model")]
    public string provider = "openai";
    public string model = "gpt-4.1-mini"; // change as needed

    [Header("Endpoint")]
    public string baseUrl = "https://api.openai.com/v1/chat/completions";

    [Header("API Key Source")]
    [Tooltip("If true, read key from Resources/<apiKeyResourcePath>.txt (TextAsset).")]
    public bool useResourcesSecret = true;

    [Tooltip("Resources path WITHOUT extension. Default: Secrets/openai_api_key")]
    public string apiKeyResourcePath = "Secrets/openai_api_key";

    [TextArea]
    [Tooltip("Fallback/API key override used if useResourcesSecret=false or resource not found.")]
    public string apiKey = "";

    [Header("Prompt Files")]
    [Tooltip("Text/YAML/TXT asset loaded from Resources (omit extension). Example: 'LLM/Prompted_Matters_base_prompt'")]
    public string basePromptResourcePath = "LLM/Prompted_Matters_base_prompt";

    [Header("Gen Settings")]
    [Range(0f,2f)] public float temperature = 0.4f;
    [Range(0, 8192)] public int maxTokens = 1400;

    [Header("Contract")]
    [Tooltip("If true, we expect a single JSON object in the assistant content.")]
    public bool expectStrictJson = true;

    /// <summary>
    /// Prefer Resources/<apiKeyResourcePath>.txt, else fall back to inline apiKey.
    /// Trims whitespace and BOM; returns "" if none found.
    /// </summary>
    public string ResolveApiKey()
    {
        if (useResourcesSecret && !string.IsNullOrWhiteSpace(apiKeyResourcePath))
        {
            var ta = Resources.Load<TextAsset>(apiKeyResourcePath);
            if (ta != null && !string.IsNullOrEmpty(ta.text))
            {
                var txt = ta.text.Trim();
                if (txt.Length > 0 && txt[0] == '\uFEFF') txt = txt.Substring(1); // strip BOM
                return txt;
            }
        }
        return string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey.Trim();
    }
}
