// Assets/Scripts/OpenAILuaGenerator.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.Networking;
using PromptedWorld;

[DisallowMultipleComponent]
public class OpenAILuaGenerator : MonoBehaviour
{
    [Header("Target Object (Lua will run here)")]
    [SerializeField] private GameObject target;             // assign in Inspector or via AssignTarget(...)
    [SerializeField] private LuaBehaviour luaBehaviour;     // auto-filled from target

    [Header("OpenAI")]
    [SerializeField] private string model = "gpt-4o-mini";
    [Tooltip("If set, overrides env var and Resources key file.")]
    [SerializeField] private string apiKeyOverride = "";

    [Header("Resources Paths (no extension)")]
    [Tooltip("System/base rules. e.g., 'LLM/base_prompt' -> Assets/Resources/LLM/base_prompt.txt")]
    [SerializeField] private string basePromptResourcePath = "LLM/base_prompt";

    [Tooltip("Per-request template. e.g., 'LLM/user_prompt_template'")]
    [SerializeField] private string userPromptTemplateResourcePath = "LLM/user_prompt_template";

    [Tooltip("Secrets key file. e.g., 'Secrets/openai_api_key'")]
    [SerializeField] private string apiKeyResourcePath = "Secrets/openai_api_key";

    [Header("User Prompt Values")]
    [TextArea(3, 8)] public string naturalLanguageIntent = "Make the object move in a circle and scale up and down smoothly.";
    public string objectDisplayName = ""; // defaults to target's name if empty

    [Header("Apply Options")]
    public bool autoApplyToLuaBehaviour = true;
    public bool callStartAfterApply = true;

    [TextArea(6, 30)] public string lastLua;

    private const string ChatUrl = "https://api.openai.com/v1/chat/completions";

    // ---------- Public API ----------
    public void AssignTarget(GameObject go)
    {
        target = go;
        luaBehaviour = EnsureLuaBehaviourOnTarget(target);
        if (string.IsNullOrWhiteSpace(objectDisplayName) && target) objectDisplayName = target.name;
    }

    [ContextMenu("Generate Lua Now")]
    public void GenerateLuaNow()
    {
        StopAllCoroutines();
        StartCoroutine(Co_GenerateLua());
    }

    // ---------- Internals ----------
    private LuaBehaviour EnsureLuaBehaviourOnTarget(GameObject go)
    {
        if (!go) return null;
        var lb = go.GetComponent<LuaBehaviour>();
        if (!lb) lb = go.AddComponent<LuaBehaviour>();
        return lb;
    }

    private IEnumerator Co_GenerateLua()
    {
        // Target & host LuaBehaviour
        if (target) luaBehaviour = EnsureLuaBehaviourOnTarget(target);
        if (!luaBehaviour) luaBehaviour = GetComponent<LuaBehaviour>();
        if (!luaBehaviour) luaBehaviour = EnsureLuaBehaviourOnTarget(gameObject);
        if (!luaBehaviour)
        {
            Debug.LogError("Failed to locate or add LuaBehaviour.");
            yield break;
        }
        if (string.IsNullOrWhiteSpace(objectDisplayName)) objectDisplayName = luaBehaviour.gameObject.name;

        // Key resolution: override -> env -> Resources
        string key = ResolveApiKey();
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("OpenAI API key not found. Set apiKeyOverride, OPENAI_API_KEY env var, or Resources/Secrets/openai_api_key.txt");
            yield break;
        }

        // Load prompts from Resources
        string basePrompt = ResourcesText.Load(basePromptResourcePath);
        if (string.IsNullOrWhiteSpace(basePrompt))
        {
            Debug.LogError($"Missing or empty base prompt at Resources/{basePromptResourcePath}.txt");
            yield break;
        }

        string userTemplate = ResourcesText.Load(userPromptTemplateResourcePath);
        if (string.IsNullOrWhiteSpace(userTemplate))
        {
            Debug.LogError($"Missing or empty user prompt template at Resources/{userPromptTemplateResourcePath}.txt");
            yield break;
        }

        string userPrompt = BuildUserPrompt(userTemplate, naturalLanguageIntent, objectDisplayName);

        // Build request
        var reqBody = new ChatRequest
        {
            model = model,
            messages = new List<Message>
            {
                new Message { role = "system", content = basePrompt },
                new Message { role = "user",   content = userPrompt }
            },
            temperature = 0.2f
        };
        string json = JsonUtility.ToJson(reqBody);

        using (var req = new UnityWebRequest(ChatUrl, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"OpenAI error: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            var resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
            string lua = ExtractFirstMessageText(resp);
            lastLua = lua;

            if (string.IsNullOrWhiteSpace(lua))
            {
                Debug.LogError("Model returned empty Lua.");
                yield break;
            }

            if (autoApplyToLuaBehaviour && luaBehaviour)
            {
                luaBehaviour.LoadScript(lua, callStartAfterApply);
                Debug.Log($"Applied Lua ({lua.Length} chars) to {luaBehaviour.gameObject.name}");
            }
            else
            {
                Debug.Log($"Lua generated ({lua.Length} chars), not auto-applied.");
            }
        }
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(apiKeyOverride)) return apiKeyOverride;

        string env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(env)) return env;

        string fromRes = ResourcesText.Load(apiKeyResourcePath);
        if (!string.IsNullOrWhiteSpace(fromRes)) return fromRes.Trim();

        return null;
    }

    private static string BuildUserPrompt(string template, string intent, string objectName)
    {
        string name = string.IsNullOrWhiteSpace(objectName) ? "Target" : objectName.Trim();
        string want = (intent ?? "").Trim();
        return template.Replace("{OBJECT_NAME}", name)
                       .Replace("{INTENT}", want);
    }

    // -------- Chat API DTOs --------
    [Serializable] private class ChatRequest
    {
        public string model;
        public List<Message> messages;
        public float temperature = 0.7f;
    }

    [Serializable] private class Message
    {
        public string role;
        public string content;
    }

    [Serializable] private class ChatResponse
    {
        public List<Choice> choices;
    }

    [Serializable] private class Choice
    {
        public Message message;
        public int index;
        public object logprobs;
        public string finish_reason;
    }

    private static string ExtractFirstMessageText(ChatResponse resp)
    {
        if (resp == null || resp.choices == null || resp.choices.Count == 0) return null;
        var m = resp.choices[0].message;
        return m != null ? m.content : null;
    }
}
