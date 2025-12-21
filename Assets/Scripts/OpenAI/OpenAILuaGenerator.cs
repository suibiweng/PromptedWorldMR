using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptedWorld;
using TMPro;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class OpenAILuaGenerator : MonoBehaviour
{
    [Header("Target Object (Lua will run here)")]
    [SerializeField] private GameObject target;
    [SerializeField] private LuaBehaviour luaBehaviour;

    [Header("Prompt Inputs")]
    [TextArea(2, 6)]
    public string naturalLanguageIntent;

    [Header("Resources Paths (no extension)")]
    [SerializeField] private string basePromptResourcePath = "LLM/base_prompt";
    [SerializeField] private string userPromptTemplateResourcePath = "LLM/user_prompt_template";
    [SerializeField] private string apiKeyResourcePath = "Secrets/openai_api_key";

    [Header("Back-Compat (LuaPromptUI expects these)")]
    public string objectDisplayName;
    public bool autoApplyToLuaBehaviour = true;
    public bool callStartAfterApply = true;

    [Header("OpenAI")]
    [SerializeField] private string apiKey;
    [SerializeField] private string model = "gpt-4.1-mini";
    [Range(0f, 2f)] public float temperature = 0.2f;

    public enum GenerationMode { Replace, EditInPlace }

    [Header("Generation Mode")]
    [SerializeField] private GenerationMode mode = GenerationMode.EditInPlace;

    public enum ReturnDisplayMode { AssistantMessage, RawJson, Off }

    [Header("Return Message (optional UI)")]
    [SerializeField] private ReturnDisplayMode displayMode = ReturnDisplayMode.AssistantMessage;
    [SerializeField] private TMP_Text returnMessageText;
    public UnityEvent<string> OnReturnMessage;

    [Header("Group Broadcast (optional)")]
    [SerializeField] private bool applyToGroup = false;
    [SerializeField] private List<GameObject> groupTargets = new List<GameObject>();

    public void EnableGroupBroadcast(bool on)
    {
        applyToGroup = on;
    }

    public void SetGroupTargets(IList<GameObject> targets)
    {
        groupTargets.Clear();
        if (targets == null) return;
        for (int i = 0; i < targets.Count; i++)
        {
            var go = targets[i];
            if (go != null)
                groupTargets.Add(go);
        }
    }

    public IReadOnlyList<GameObject> GroupTargets => groupTargets;

    private string _activeLogId = null;
    private float _rtStartTime = 0f;

    [NonSerialized] public string lastGeneratedLua = "";
    [NonSerialized] public string lastAssistantMessage = "";
    [NonSerialized] public string lastRawJson = "";
    [NonSerialized] public string lastError = "";

    // -------- public API --------

    public void AssignTarget(GameObject go)
    {
        target = go;
        luaBehaviour = (go != null) ? go.GetComponent<LuaBehaviour>() : null;
    }

    public void SetIntent(string intent)
    {
        naturalLanguageIntent = intent;
    }

    public void GenerateLuaNow()
    {
        StartGeneration();
    }

    public void StartGeneration()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
        {
            ShowReturn("[LuaGen] Empty intent – type a prompt first.", isError: true);
            return;
        }

        StartCoroutine(Co_GenerateLua());
    }

    // -------- DTOs --------
    [Serializable] private class Message { public string role; public string content; }

    [Serializable] private class ChatRequest
    {
        public string model;
        public float temperature;
        public List<Message> messages;
    }

    [Serializable] private class Choice { public Message message; }

    [Serializable] private class Usage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [Serializable] private class ChatResponse
    {
        public string id;
        public long created;
        public string model;
        public Usage usage;
        public List<Choice> choices;
    }

    // -------- main coroutine --------

    private IEnumerator Co_GenerateLua()
    {
        if (target == null)
        {
            ShowReturn("[LuaGen] No target GameObject assigned.", isError: true);
            yield break;
        }

        if (luaBehaviour == null)
            luaBehaviour = target.GetComponent<LuaBehaviour>();

        string basePrompt = LoadTextResource(basePromptResourcePath);
        string template   = LoadTextResource(userPromptTemplateResourcePath);
        string key        = LoadTextResource(apiKeyResourcePath);

        if (!string.IsNullOrEmpty(key))
            apiKey = key.Trim();

        if (string.IsNullOrEmpty(basePrompt) || string.IsNullOrEmpty(template))
        {
            ShowReturn($"[LuaGen] Missing base/template text. base='{basePromptResourcePath}' template='{userPromptTemplateResourcePath}'", isError: true);
            yield break;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowReturn("[LuaGen] Missing API key (Resources or Inspector).", isError: true);
            yield break;
        }

        // logging
        ProgramableObject po = target.GetComponent<ProgramableObject>();
        string objName = !string.IsNullOrWhiteSpace(objectDisplayName)
            ? objectDisplayName
            : target.name;

        string currentLua = "";
        if (mode == GenerationMode.EditInPlace && luaBehaviour != null)
        {
            currentLua = luaBehaviour.CurrentLua ?? "";
        }

        string userPrompt = BuildUserPrompt(template, naturalLanguageIntent, objName, currentLua);

        _activeLogId = null;
        _rtStartTime = Time.realtimeSinceStartup;

        if (po != null)
        {
            _activeLogId = po.BeginPromptLog(naturalLanguageIntent, mode.ToString(), model);
        }

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
                ShowReturn($"HTTP error: {www.error}", isError: true);
                yield break;
            }

            lastRawJson = www.downloadHandler.text;

            ChatResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ChatResponse>(lastRawJson);
            }
            catch (Exception ex)
            {
                __RecordFailure(po, $"Parse error: {ex.Message}");
                ShowReturn($"Parse error: {ex.Message}", isError: true);
                yield break;
            }

            // ---- NEW: clean Lua source before applying ----
            string rawLuaText = ExtractFirstMessageText(resp);
            if (string.IsNullOrWhiteSpace(rawLuaText))
            {
                __RecordFailure(po, "Empty Lua in response");
                ShowReturn("Empty Lua in response", isError: true);
                yield break;
            }

            // Keep raw assistant message for debugging
            lastAssistantMessage = rawLuaText;

            // Clean up quotes, ``` fences, stray backticks, junk before first 'function'
            string luaText = LuaSourceCleaner.Clean(rawLuaText);

            if (string.IsNullOrWhiteSpace(luaText))
            {
                __RecordFailure(po, "Lua became empty after cleaning");
                ShowReturn("Lua became empty after cleaning", isError: true);
                yield break;
            }

            lastGeneratedLua = luaText;

            // optional display
            if (displayMode != ReturnDisplayMode.Off)
            {
                if (displayMode == ReturnDisplayMode.AssistantMessage)
                    ShowReturn(lastAssistantMessage);
                else if (displayMode == ReturnDisplayMode.RawJson)
                    ShowReturn(lastRawJson);
            }

            if (!autoApplyToLuaBehaviour)
            {
                if (po != null && !string.IsNullOrEmpty(_activeLogId))
                {
                    float dt = Time.realtimeSinceStartup - _rtStartTime;
                    int inTok  = resp?.usage?.prompt_tokens     ?? 0;
                    int outTok = resp?.usage?.completion_tokens ?? 0;
                    po.CompletePromptLogSuccess(_activeLogId, luaText, dt, inTok, outTok);
                    _activeLogId = null;
                }
                yield break;
            }

            // ensure main target has LuaBehaviour
            if (luaBehaviour == null)
            {
                luaBehaviour = target.GetComponent<LuaBehaviour>();
                if (luaBehaviour == null)
                {
                    // auto-add component if missing
                    luaBehaviour = target.AddComponent<LuaBehaviour>();
                }
            }

            try
            {
                // apply to main target (CLEANED luaText)
                luaBehaviour.LoadScript(luaText);
                if (callStartAfterApply)
                {
                    luaBehaviour.StartRun();
                }

                if (po != null && !string.IsNullOrEmpty(_activeLogId))
                {
                    float dt = Time.realtimeSinceStartup - _rtStartTime;
                    int inTok  = resp?.usage?.prompt_tokens     ?? 0;
                    int outTok = resp?.usage?.completion_tokens ?? 0;
                    po.CompletePromptLogSuccess(_activeLogId, luaText, dt, inTok, outTok);
                    _activeLogId = null;
                }

                // group broadcast
                if (applyToGroup && groupTargets != null && groupTargets.Count > 0)
                {
                    Debug.Log("[LuaGen] Applying Lua to group of " + groupTargets.Count + " objects.");
                    for (int i = 0; i < groupTargets.Count; i++)
                    {
                        var go = groupTargets[i];
                        if (go == null) continue;
                        if (go == target) continue; // already done

                        try
                        {
                            var otherLb = go.GetComponent<LuaBehaviour>();
                            if (otherLb == null)
                                otherLb = go.AddComponent<LuaBehaviour>();

                            otherLb.LoadScript(luaText);
                            if (callStartAfterApply)
                            {
                                otherLb.StartRun();
                            }
                        }
                        catch (Exception ex2)
                        {
                            Debug.LogError("[LuaGen] Failed to apply group Lua to " + go.name + ": " + ex2.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                __RecordFailure(po, $"Apply error: {ex.Message}");
                ShowReturn($"Apply error: {ex.Message}", isError: true);
                yield break;
            }
        }
    }

    // -------- helpers --------

    private static string BuildUserPrompt(string template, string intent, string objectName, string currentLua)
    {
        string name = string.IsNullOrWhiteSpace(objectName) ? "Target" : objectName.Trim();
        string want = (intent ?? "").Trim();
        string cur  = (currentLua ?? "").Trim();

        return template.Replace("{OBJECT_NAME}", name)
                       .Replace("{INTENT}",      want)
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
        if (isError) lastError = text;

        if (returnMessageText != null)
            returnMessageText.text = text ?? "";

        OnReturnMessage?.Invoke(text ?? "");

        if (isError) Debug.LogError("[LuaGen] " + text);
        else Debug.Log("[LuaGen] " + text);
    }

    // -------- Lua source cleaner (envelope only, does NOT change code semantics) --------

    private static class LuaSourceCleaner
    {
        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // 1. Trim whitespace
            var s = raw.Trim();

            // 2. Strip outer quotes if entire payload is wrapped in "..." or '...'
            if ((s.StartsWith("\"") && s.EndsWith("\"")) ||
                (s.StartsWith("'") && s.EndsWith("'")))
            {
                // Only unwrap if it looks like code (has newline or 'function')
                if (s.Length > 2 && (s.Contains("\n") || s.Contains("function")))
                {
                    s = s.Substring(1, s.Length - 2).Trim();
                }
            }

            // 3. Remove leading/trailing markdown fences and stray backticks
            var lines = s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var list = new List<string>(lines);

            // Strip leading fence-like lines
            while (list.Count > 0)
            {
                var first = list[0].Trim();
                if (first == "```" ||
                    first.Equals("```lua", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("```Lua", StringComparison.OrdinalIgnoreCase) ||
                    first == "`")
                {
                    list.RemoveAt(0);
                }
                else
                {
                    break;
                }
            }

            // Strip trailing fence-like lines
            while (list.Count > 0)
            {
                var last = list[list.Count - 1].Trim();
                if (last == "```" || last == "`")
                {
                    list.RemoveAt(list.Count - 1);
                }
                else
                {
                    break;
                }
            }

            s = string.Join("\n", list).Trim();

            // 4. Last resort: if there is junk before first 'function', drop it
            int idx = s.IndexOf("function", StringComparison.Ordinal);
            if (idx > 0)
            {
                s = s.Substring(idx).TrimStart();
            }

            return s;
        }
    }
}
