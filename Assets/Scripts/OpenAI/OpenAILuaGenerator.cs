// Assets/Scripts/LuaRuntime/OpenAILuaGenerator.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptedWorld; // for PromptedWorldManager reference (optional)
using TMPro;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class OpenAILuaGenerator : MonoBehaviour
{
    [Header("Target Object (Lua will run here)")]
    [SerializeField] private GameObject target;             // assign in Inspector or via AssignTarget(...)
    [SerializeField] private LuaBehaviour luaBehaviour;     // auto-filled from target

    [Header("Prompt Inputs")]
    [TextArea(2, 6)] public string naturalLanguageIntent;   // user text

    [Header("Resources Paths (no extension)")]
    [Tooltip("System/base rules. e.g., 'LLM/base_prompt' -> Assets/Resources/LLM/base_prompt.txt")]
    [SerializeField] private string basePromptResourcePath = "LLM/base_prompt";

    [Tooltip("Per-request template. e.g., 'LLM/user_prompt_template'")]
    [SerializeField] private string userPromptTemplateResourcePath = "LLM/user_prompt_template";

    [Tooltip("Secrets key file. e.g., 'Secrets/openai_api_key'")]
    [SerializeField] private string apiKeyResourcePath = "Secrets/openai_api_key";

    // === Back-compat fields expected by LuaPromptUI ===
    [Header("Back-Compat (LuaPromptUI expects these)")]
    public string objectDisplayName;                        // if empty, falls back to target.name
    public bool autoApplyToLuaBehaviour = true;             // if false, we won't LoadScript; just store lastGeneratedLua
    public bool callStartAfterApply = true;                 // kept for compatibility; LuaBehaviour.LoadScript already calls start()

    [Header("OpenAI")]
    [SerializeField] private string apiKey;                 // fallback if resource missing
    [SerializeField] private string model = "gpt-4.1-mini";
    [Range(0f, 2f)] public float temperature = 0.2f;

    public enum GenerationMode { Replace, EditInPlace }

    [Header("Generation Mode")]
    [SerializeField] private GenerationMode mode = GenerationMode.EditInPlace;

    // NEW: Return message display
    public enum ReturnDisplayMode { AssistantMessage, RawJson, Off }

    [Header("Return Message (optional UI)")]
    [SerializeField] private ReturnDisplayMode displayMode = ReturnDisplayMode.AssistantMessage;
    [Tooltip("Where to print the return message (Assistant text or Raw JSON). Optional.")]
    [SerializeField] private TMP_Text returnMessageText;
    [Tooltip("Also emit the return message (string) here if you want to route it elsewhere.")]
    public UnityEvent<string> OnReturnMessage;

    // Runtime helpers
    private string _activeLogId = null;
    private float  _rtStartTime = 0f;

    // Optional manager ref (not required)
    private PromptedWorldManager pwm;

    // For cases where autoApplyToLuaBehaviour == false
    [NonSerialized] public string lastGeneratedLua = "";

    // For display/inspection
    [NonSerialized] public string lastAssistantMessage = ""; // same as lastGeneratedLua but kept for clarity
    [NonSerialized] public string lastRawJson = "";
    [NonSerialized] public string lastError = "";

    // ====== Public API ======
    public void AssignTarget(GameObject go)
    {
        target = go;
        luaBehaviour = (go != null) ? go.GetComponent<LuaBehaviour>() : null;
    }

    public void SetIntent(string intent)
    {
        naturalLanguageIntent = intent;
    }

    // Back-compat: LuaPromptUI calls this
    public void GenerateLuaNow()
    {
        StartGeneration();
    }

    public void StartGeneration()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
        {
            Debug.LogWarning("[LuaGen] Empty intent.");
            return;
        }
        StartCoroutine(Co_GenerateLua());
    }

    // ====== Request / Response DTOs ======
    [Serializable] private class Message { public string role; public string content; }
    [Serializable] private class ChatRequest
    {
        public string model;
        public float temperature;
        public List<Message> messages;
    }
    [Serializable] private class Choice   { public Message message; }
    [Serializable] private class Usage    { public int prompt_tokens; public int completion_tokens; public int total_tokens; }
    [Serializable] private class ChatResponse
    {
        public string id;
        public long created;
        public string model;
        public Usage usage;
        public List<Choice> choices;
    }

    // ====== Coroutine ======
    private IEnumerator Co_GenerateLua()
    {
        // Resolve target / behaviour
        if (luaBehaviour == null && target != null)
            luaBehaviour = target.GetComponent<LuaBehaviour>();

        if (target == null)
        {
            ShowReturn("[LuaGen] No target GameObject assigned.", isError:true);
            yield break;
        }

        // Load prompts & API key from Resources (with graceful fallbacks)
        string basePrompt = LoadTextResource(basePromptResourcePath);
        string template   = LoadTextResource(userPromptTemplateResourcePath);
        string key        = LoadTextResource(apiKeyResourcePath);

        if (!string.IsNullOrEmpty(key))
            apiKey = key.Trim(); // strip whitespace/newlines at ends

        if (string.IsNullOrEmpty(basePrompt) || string.IsNullOrEmpty(template))
        {
            ShowReturn($"[LuaGen] Missing base or template. base='{basePromptResourcePath}' template='{userPromptTemplateResourcePath}'", isError:true);
            yield break;
        }
        if (string.IsNullOrEmpty(apiKey))
        {
            ShowReturn("[LuaGen] Missing API key (Resources or inspector).", isError:true);
            yield break;
        }

        // Name used in the prompt
        string objName = !string.IsNullOrWhiteSpace(objectDisplayName)
            ? objectDisplayName
            : (target != null ? target.name : "Target");

        // Provide CURRENT_LUA when editing
        string currentLua = "";
        if (mode == GenerationMode.EditInPlace && luaBehaviour != null)
            currentLua = luaBehaviour.CurrentLua ?? "";

        string userPrompt = BuildUserPrompt(template, naturalLanguageIntent, objName, currentLua);

        // Begin per-object log if available
        _activeLogId = null;
        _rtStartTime = Time.realtimeSinceStartup;
        ProgramableObject po = target.GetComponent<ProgramableObject>();
        if (po != null)
            _activeLogId = po.BeginPromptLog(naturalLanguageIntent, mode.ToString(), model);

        // Build request
        var reqObj = new ChatRequest
        {
            model = model,
            temperature = temperature,
            messages = new List<Message>
            {
                new Message { role = "system", content = basePrompt },
                new Message { role = "user",   content = userPrompt }
            }
        };

        string json = JsonUtility.ToJson(reqObj);

        using (var www = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                __RecordFailure(po, $"HTTP error: {www.error}");
                ShowReturn($"HTTP error: {www.error}", isError:true);
                yield break;
            }

            lastRawJson = www.downloadHandler.text; // keep raw
            ChatResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ChatResponse>(lastRawJson);
            }
            catch (Exception ex)
            {
                __RecordFailure(po, $"Parse error: {ex.Message}");
                ShowReturn($"Parse error: {ex.Message}", isError:true);
                yield break;
            }

            string luaText = ExtractFirstMessageText(resp);
            if (string.IsNullOrWhiteSpace(luaText))
            {
                __RecordFailure(po, "Empty Lua in response");
                ShowReturn("Empty Lua in response", isError:true);
                yield break;
            }

            // Decide whether to apply or just store
            lastAssistantMessage = luaText;
            lastGeneratedLua = luaText;

            // Show (Assistant or Raw) based on mode
            if (displayMode != ReturnDisplayMode.Off)
            {
                if (displayMode == ReturnDisplayMode.AssistantMessage)
                    ShowReturn(lastAssistantMessage);
                else if (displayMode == ReturnDisplayMode.RawJson)
                    ShowReturn(lastRawJson);
            }

            if (!autoApplyToLuaBehaviour)
            {
                // Not applying; finalize log success with the generated script
                if (po != null && !string.IsNullOrEmpty(_activeLogId))
                {
                    float dt = Time.realtimeSinceStartup - _rtStartTime;
                    // If usage exists, include tokens
                    int inTok = resp?.usage?.prompt_tokens ?? 0;
                    int outTok = resp?.usage?.completion_tokens ?? 0;
                    po.CompletePromptLogSuccess(_activeLogId, luaText, dt, inTok, outTok);
                    _activeLogId = null;
                }
                yield break;
            }

            // Apply to LuaBehaviour (required for live updates)
            if (luaBehaviour == null)
            {
                luaBehaviour = target.GetComponent<LuaBehaviour>();
                if (luaBehaviour == null)
                {
                    __RecordFailure(po, "No LuaBehaviour on target to apply the script.");
                    ShowReturn("No LuaBehaviour on target to apply the script.", isError:true);
                    yield break;
                }
            }

            try
            {
                // Note: LuaBehaviour.LoadScript() does not auto-call start() in the newer version.
                // If your LuaBehaviour still auto-calls start(), that's fine; this remains compatible.
                luaBehaviour.LoadScript(luaText);

                if (po != null && !string.IsNullOrEmpty(_activeLogId))
                {
                    float dt = Time.realtimeSinceStartup - _rtStartTime;
                    int inTok = resp?.usage?.prompt_tokens ?? 0;
                    int outTok = resp?.usage?.completion_tokens ?? 0;
                    po.CompletePromptLogSuccess(_activeLogId, luaText, dt, inTok, outTok);
                    _activeLogId = null;
                }
            }
            catch (Exception ex)
            {
                __RecordFailure(po, $"Apply error: {ex.Message}");
                ShowReturn($"Apply error: {ex.Message}", isError:true);
                yield break;
            }
        }
    }

    // ====== Helpers ======
    private static string BuildUserPrompt(string template, string intent, string objectName, string currentLua)
    {
        string name = string.IsNullOrWhiteSpace(objectName) ? "Target" : objectName.Trim();
        string want = (intent ?? "").Trim();
        string cur  = (currentLua ?? "").Trim();
        return template.Replace("{OBJECT_NAME}", name)
                       .Replace("{INTENT}", want)
                       .Replace("{CURRENT_LUA}", cur);
    }

    private static string ExtractFirstMessageText(ChatResponse resp)
    {
        if (resp == null || resp.choices == null || resp.choices.Count == 0) return null;
        var m = resp.choices[0].message;
        return m != null ? m.content : null;
    }

    private static string LoadTextResource(string pathNoExt)
    {
        if (string.IsNullOrWhiteSpace(pathNoExt)) return "";
        var ta = Resources.Load<TextAsset>(pathNoExt);
        return ta != null ? ta.text : "";
    }

    private void __RecordFailure(ProgramableObject po, string msg)
    {
        Debug.LogError("[LuaGen] " + msg);
        lastError = msg;
        if (po != null && !string.IsNullOrEmpty(_activeLogId))
        {
            float dt = Time.realtimeSinceStartup - _rtStartTime;
            po.CompletePromptLogFailure(_activeLogId, msg, dt);
            _activeLogId = null;
        }
    }

    private void ShowReturn(string text, bool isError = false)
    {
        // Store last error/message
        if (isError) lastError = text;

        // Print to optional UI
        if (returnMessageText != null)
            returnMessageText.text = text ?? "";

        // Fire event so you can route elsewhere
        OnReturnMessage?.Invoke(text ?? "");

        // Also log to console
        if (isError) Debug.LogError("[LuaGen] " + text);
        else Debug.Log("[LuaGen] " + text);
    }
}
