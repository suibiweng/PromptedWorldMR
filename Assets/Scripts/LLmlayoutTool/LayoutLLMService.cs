using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Service that sends layout prompts to OpenAI and returns a JSON layout string.
/// </summary>
public class LayoutLLMService : MonoBehaviour
{
    [Header("API Settings")]
    [Tooltip("OpenAI API base URL for chat completions.")]
    public string chatUrl = "https://api.openai.com/v1/chat/completions";

    [Tooltip("Model name, e.g., 'gpt-4.1-mini' or 'gpt-4.1'.")]
    public string modelName = "gpt-4.1-mini";

    [Tooltip("Path inside Resources folder to a text asset containing your API key.\nExample: 'Secrets/openai_api_key' (no extension).")]
    public string apiKeyResourcePath = "Secrets/openai_api_key";

    [Tooltip("Max tokens for the completion. Exposed publicly so you can tune it in the Inspector.")]
    public int maxTokens = 10000;

    [Tooltip("Optional: log full request/response JSON to the console.")]
    public bool verboseLogging = true;

    private string _apiKey;

    /// <summary>
    /// Last raw response from the LLM (for debugging).
    /// </summary>
    [TextArea(3, 20)]
    public string lastRawResponse;

    private void Awake()
    {
        EnsureApiKeyLoaded();
    }

    private void EnsureApiKeyLoaded()
    {
        if (!string.IsNullOrEmpty(_apiKey)) return;

        TextAsset keyAsset = Resources.Load<TextAsset>(apiKeyResourcePath);
        if (keyAsset == null)
        {
            Debug.LogError($"[LayoutLLMService] Failed to load API key from Resources at '{apiKeyResourcePath}'. " +
                           "Create a TextAsset there with your API key.");
            return;
        }

        _apiKey = keyAsset.text.Trim();
        if (verboseLogging)
        {
            Debug.Log($"[LayoutLLMService] Loaded API key from Resources: {apiKeyResourcePath}");
        }
    }

    /// <summary>
    /// System prompt that defines the JSON schema and scale behavior.
    /// </summary>
    private string GetSystemPrompt()
    {
        return
            "You are a layout planner for Unity.\n" +
            "You MUST respond with only a single JSON object and nothing else.\n" +
            "The JSON must follow this schema:\n" +
            "{\n" +
            "  \"layout_name\": \"string\",\n" +
            "  \"space\": \"local\",\n" +
            "  \"instances\": [\n" +
            "    {\n" +
            "      \"id\": \"obj_0\",\n" +
            "      \"position\": { \"x\": 0.0, \"y\": 0.0, \"z\": 0.0 },\n" +
            "      \"rotationEuler\": { \"x\": 0.0, \"y\": 0.0, \"z\": 0.0 },\n" +
            "      \"scale\": { \"x\": 1.0, \"y\": 1.0, \"z\": 1.0 }\n" +
            "    }\n" +
            "  ]\n" +
            "}\n" +
            "- Do NOT include ```json fences.\n" +
            "- Do NOT include explanations.\n" +
            "- Keep the JSON as short as possible while valid.\n" +
            "- `space` must be \"local\".\n" +
            "- `position` is typically on the XZ plane (y=0) unless vertical stacking is requested.\n" +
            "- `rotationEuler` uses degrees, Unity's X,Y,Z order.\n" +
            "- IMPORTANT: `scale` is a LOCAL SCALE MULTIPLIER.\n" +
            "  - By default, use { \"x\":1, \"y\":1, \"z\":1 } for all instances.\n" +
            "  - ONLY change scale if the user explicitly asks to change object size (bigger, smaller, taller, etc.).\n" +
            "- Use ids like \"obj_0\", \"obj_1\", \"obj_2\", etc. for each instance.\n";
    }

    // ---------- Serializable request/response types ----------

    [Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature;
        public int max_tokens;
    }

    [Serializable]
    private class ChatCompletionResponse
    {
        [Serializable]
        public class ChoiceMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        public class Choice
        {
            public int index;
            public ChoiceMessage message;
            public string finish_reason;
        }

        public string id;
        public string model;
        public Choice[] choices;
    }

    /// <summary>
    /// Sends a prompt to the LLM and returns a JSON layout string via callback.
    /// </summary>
    public IEnumerator RequestLayoutJson(string userPrompt, Action<string> onJson, Action<string> onError)
    {
        EnsureApiKeyLoaded();
        if (string.IsNullOrEmpty(_apiKey))
        {
            string err = "[LayoutLLMService] No API key loaded.";
            Debug.LogError(err);
            onError?.Invoke(err);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            string err = "[LayoutLLMService] modelName is empty. Set it in the Inspector (e.g., 'gpt-4.1-mini').";
            Debug.LogError(err);
            onError?.Invoke(err);
            yield break;
        }

        if (verboseLogging)
        {
            Debug.Log("[LayoutLLMService] Using URL: " + chatUrl);
        }

        // Build request object
        var request = new ChatRequest
        {
            model = modelName,
            messages = new[]
            {
                new ChatMessage
                {
                    role = "system",
                    content = GetSystemPrompt()
                },
                new ChatMessage
                {
                    role = "user",
                    content = userPrompt
                }
            },
            temperature = 0.1f,
            max_tokens = Mathf.Max(1, maxTokens) // ensure > 0 but otherwise use whatever you set
        };

        string jsonBody = JsonUtility.ToJson(request);

        if (verboseLogging)
        {
            Debug.Log("[LayoutLLMService] Chat request body: " + jsonBody);
        }

        using (UnityWebRequest req = new UnityWebRequest(chatUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _apiKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = "[LayoutLLMService] Error: " + req.error + "\n" + req.downloadHandler.text;
                Debug.LogError(err);
                onError?.Invoke(err);
                yield break;
            }

            string raw = req.downloadHandler.text;
            lastRawResponse = raw;

            if (verboseLogging)
            {
                Debug.Log("[LayoutLLMService] Raw chat response: " + raw);
            }

            string extractedJson = ExtractJsonFromChatResponse(raw);
            if (string.IsNullOrEmpty(extractedJson))
            {
                string err = "[LayoutLLMService] Failed to extract JSON from response.";
                Debug.LogError(err);
                onError?.Invoke(err);
                yield break;
            }

            if (verboseLogging)
            {
                Debug.Log("[LayoutLLMService] Extracted JSON:\n" + extractedJson);
            }

            onJson?.Invoke(extractedJson);
        }
    }

    /// <summary>
    /// Extracts the assistant's message.content and slices out the JSON object.
    /// </summary>
    private string ExtractJsonFromChatResponse(string raw)
    {
        try
        {
            ChatCompletionResponse resp = JsonUtility.FromJson<ChatCompletionResponse>(raw);
            if (resp == null || resp.choices == null || resp.choices.Length == 0)
                return null;

            string content = resp.choices[0].message.content;
            if (string.IsNullOrEmpty(content))
                return null;

            int firstBrace = content.IndexOf('{');
            int lastBrace = content.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace < firstBrace)
                return null;

            string json = content.Substring(firstBrace, lastBrace - firstBrace + 1);
            return json.Trim();
        }
        catch (Exception e)
        {
            Debug.LogError("[LayoutLLMService] Exception in ExtractJsonFromChatResponse: " + e);
            return null;
        }
    }
}
