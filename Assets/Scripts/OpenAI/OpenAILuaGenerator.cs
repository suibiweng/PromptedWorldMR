// Assets/Scripts/LuaRuntime/OpenAILuaGenerator.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class OpenAILuaGenerator : MonoBehaviour
{
    [Header("Target Object (Lua will run here)")]
    [SerializeField] private GameObject target;             // assign in Inspector or via AssignTarget(...)
    [SerializeField] private LuaBehaviour luaBehaviour;     // auto-filled from target

    [Header("Prompt Inputs")]
    [TextArea(2, 6)] public string naturalLanguageIntent;   // user text
    public TextAsset basePromptText;                        // Resources: base_prompt.txt (or any TextAsset)
    public TextAsset userTemplateText;                      // Resources: user_prompt_template.txt

    // === Back-compat fields expected by LuaPromptUI ===
    [Header("Back-Compat (LuaPromptUI expects these)")]
    public string objectDisplayName;                        // if empty, falls back to target.name
    public bool autoApplyToLuaBehaviour = true;             // if false, we won't LoadScript; just store lastGeneratedLua
    public bool callStartAfterApply = true;                 // kept for compatibility; note: LuaBehaviour.LoadScript already calls start()

    [Header("OpenAI")]
    [SerializeField] private string apiKey;                 // set via inspector or env var
    [SerializeField] private string model = "gpt-4.1-mini";
    [Range(0f, 2f)] public float temperature = 0.2f;

    public enum GenerationMode { Replace, EditInPlace }

    [Header("Generation Mode")]
    [SerializeField] private GenerationMode mode = GenerationMode.EditInPlace;

    // Runtime helpers
    private string _activeLogId = null;
    private float  _rtStartTime = 0f;

    // For cases where autoApplyToLuaBehaviour == false
    [NonSerialized] public string lastGeneratedLua = "";

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

    // ====== Request / Response DTOs (simple) ======
    [Serializable] private class Message { public string role; public string content; }
    [Serializable] private class ChatRequest
    {
        public string model;
        public float temperature;
        public List<Message> messages;
    }
    [Serializable] private class Choice   { public Message message; }
    [Serializable] private class ChatResponse { public List<Choice> choices; }

    // ====== Coroutine ======
    private IEnumerator Co_GenerateLua()
    {
        // Resolve target
        if (luaBehaviour == null && target != null)
            luaBehaviour = target.GetComponent<LuaBehaviour>();

        if (target == null)
        {
            Debug.LogError("[LuaGen] No target GameObject assigned.");
            yield break;
        }

        // Build system/user prompts
        string basePrompt = basePromptText != null ? basePromptText.text : "";
        string template   = userTemplateText != null ? userTemplateText.text : "";
        if (string.IsNullOrEmpty(basePrompt) || string.IsNullOrEmpty(template))
        {
            Debug.LogError("[LuaGen] Missing basePromptText or userTemplateText.");
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
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                __RecordFailure(po, $"HTTP error: {www.error}");
                yield break;
            }

            ChatResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);
            }
            catch (Exception ex)
            {
                __RecordFailure(po, $"Parse error: {ex.Message}");
                yield break;
            }

            string luaText = ExtractFirstMessageText(resp);
            if (string.IsNullOrWhiteSpace(luaText))
            {
                __RecordFailure(po, "Empty Lua in response");
                yield break;
            }

            // Decide whether to apply or just store
            lastGeneratedLua = luaText;

            if (!autoApplyToLuaBehaviour)
            {
                // Not applying; just finalize log success with the generated script
                if (po != null && !string.IsNullOrEmpty(_activeLogId))
                {
                    float dt = Time.realtimeSinceStartup - _rtStartTime;
                    po.CompletePromptLogSuccess(_activeLogId, luaText, dt, 0, 0);
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
                    yield break;
                }
            }

            try
            {
                // Note: LuaBehaviour.LoadScript() already calls start()
                // The 'callStartAfterApply' flag is kept for back-compat with your UI,
                // but we can't suppress LoadScript's internal start() without changing LuaBehaviour.
                luaBehaviour.LoadScript(luaText);

                if (po != null && !string.IsNullOrEmpty(_activeLogId))
                {
                    float dt = Time.realtimeSinceStartup - _rtStartTime;
                    po.CompletePromptLogSuccess(_activeLogId, luaText, dt, 0, 0);
                    _activeLogId = null;
                }
            }
            catch (Exception ex)
            {
                __RecordFailure(po, $"Apply error: {ex.Message}");
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

    private void __RecordFailure(ProgramableObject po, string msg)
    {
        Debug.LogError("[LuaGen] " + msg);
        if (po != null && !string.IsNullOrEmpty(_activeLogId))
        {
            float dt = Time.realtimeSinceStartup - _rtStartTime;
            po.CompletePromptLogFailure(_activeLogId, msg, dt);
            _activeLogId = null;
        }
    }
}
