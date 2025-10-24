// OpenAIProfileGenerator.cs
// Builds prompt, calls LLM, parses JSON, applies to PromptedMatter, and supports "continue editing".

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class OpenAIProfileGenerator : MonoBehaviour
{
    [Header("Profile")]
    public LLMProfile llmProfile;

    [Header("Wiring")]
    public PromptedMatter targetMatter;

    [Header("Options")]
    public bool applyLua = true;
    public bool applyParticles = true;

    [Tooltip("If true, include previously applied Lua and particle JSON in the system context so the model continues editing.")]
    public bool continueEditing = true;

    [Header("Debug")]
    [TextArea] public string lastRawAssistant;

    [Serializable] public class GeneratedProfile
    {
        public string object_name;
        public string lua_code;
        public ParticleProfile particle_json;
        public SerializableDict param_ui_lua = new SerializableDict();
        public SerializableDict param_ui_particle = new SerializableDict();
        public string notes = "";
        public string[] errors = Array.Empty<string>();
    }

    [Serializable] public class SerializableDict : UnityEngine.ISerializationCallbackReceiver
    {
        public System.Collections.Generic.List<string> keys = new();
        public System.Collections.Generic.List<string> values = new();
        private System.Collections.Generic.Dictionary<string, string> _dict = new();

        public void OnBeforeSerialize()
        {
            keys.Clear(); values.Clear();
            foreach (var kv in _dict) { keys.Add(kv.Key); values.Add(kv.Value); }
        }
        public void OnAfterDeserialize()
        {
            _dict = new();
            for (int i = 0; i < Math.Min(keys.Count, values.Count); i++)
                _dict[keys[i]] = values[i];
        }
        public System.Collections.Generic.Dictionary<string, string> ToDict() => _dict;
        public void Set(string k, string v) => _dict[k] = v;
    }

    // Proper payload classes for JsonUtility
    [Serializable] private class ChatMessage { public string role; public string content; public ChatMessage(string role, string content){ this.role=role; this.content=content; } }
    [Serializable] private class ChatPayload
    {
        public string model;
        public float temperature;
        public int max_tokens;
        public ChatMessage[] messages;
    }
    [Serializable] private class OpenAIChatResponse
    {
        [Serializable] public class Message { public string role; public string content; }
        [Serializable] public class Choice { public Message message; }
        public Choice[] choices;
    }

    public void GenerateFromUserPrompt(string userPrompt)
    {
        string context = targetMatter ? targetMatter.GetLLMContext() : "object_profile:{}";
        StartCoroutine(CoGenerate(context, userPrompt));
    }

    public void GenerateWithExplicitContext(string objectContext, string userPrompt)
    {
        StartCoroutine(CoGenerate(objectContext, userPrompt));
    }

    private IEnumerator CoGenerate(string objectContext, string userPrompt)
    {
        if (llmProfile == null) { Debug.LogError("[OpenAIProfileGenerator] Missing LLMProfile."); yield break; }
        if (string.IsNullOrWhiteSpace(llmProfile.model)) { Debug.LogError("[OpenAIProfileGenerator] LLMProfile.model is empty."); yield break; }

        var basePrompt = LoadBasePrompt(llmProfile.basePromptResourcePath);
        if (string.IsNullOrEmpty(basePrompt)) { Debug.LogError("[OpenAIProfileGenerator] Base prompt not found via Resources at '" + llmProfile.basePromptResourcePath + "'."); yield break; }

        var apiKey = llmProfile.ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey)) { Debug.LogError("[OpenAIProfileGenerator] API key is empty."); yield break; }

        // Build system message with optional previous state
        string previousBlock = (continueEditing && targetMatter != null) ? ("\n\n" + targetMatter.GetPreviousStateContext()) : "";
        string systemMsg = basePrompt + "\n\n" + objectContext + previousBlock;
        string userMsg = userPrompt;

        yield return SendChatAsync(systemMsg, userMsg, apiKey, (ok, contentOrErr) =>
        {
            if (!ok)
            {
                Debug.LogError($"[OpenAIProfileGenerator] Generation failed: {contentOrErr}");
                return;
            }

            lastRawAssistant = contentOrErr;

            try
            {
                var prof = JsonUtility.FromJson<GeneratedProfile>(contentOrErr);
                if (prof == null) throw new Exception("Parsed profile is null");

                if (targetMatter != null)
                {
                    if (applyLua && !string.IsNullOrEmpty(prof.lua_code))
                        targetMatter.ApplyLua(prof.lua_code);

                    if (applyParticles && prof.particle_json != null)
                        targetMatter.ApplyParticles(prof.particle_json);

                    // remember what was applied for future edits
                    targetMatter.RememberLast(prof.lua_code, prof.particle_json);
                }

                Debug.Log("[OpenAIProfileGenerator] Applied generated profile.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenAIProfileGenerator] JSON parse/apply error: {ex.Message}\nRaw:\n{contentOrErr}");
            }
        });
    }

    private string LoadBasePrompt(string resourcePath)
    {
        var textAsset = Resources.Load<TextAsset>(resourcePath);
        return textAsset ? textAsset.text : null;
    }

    private IEnumerator SendChatAsync(string systemMsg, string userMsg, string apiKey, Action<bool, string> done)
    {
        var payload = new ChatPayload
        {
            model = llmProfile.model,
            temperature = llmProfile.temperature,
            max_tokens = llmProfile.maxTokens,
            messages = new[]
            {
                new ChatMessage("system", systemMsg),
                new ChatMessage("user", userMsg)
            }
        };
        string json = JsonUtility.ToJson(payload);

        using var req = new UnityWebRequest(llmProfile.baseUrl, "POST");
        byte[] body = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            done(false, req.error + "\n" + req.downloadHandler.text);
            yield break;
        }

        try
        {
            var root = JsonUtility.FromJson<OpenAIChatResponse>(req.downloadHandler.text);
            if (root == null || root.choices == null || root.choices.Length == 0 || root.choices[0].message == null)
            {
                done(false, "Unexpected response format:\n" + req.downloadHandler.text);
                yield break; // <-- FIX: use yield break inside IEnumerator
            }

            var content = root.choices[0].message.content;
            if (llmProfile.expectStrictJson) content = StripToFirstJsonObject(content);
            done(true, content);
        }
        catch (Exception ex)
        {
            done(false, "Parse response error: " + ex.Message + "\n" + req.downloadHandler.text);
        }
    }

    private string StripToFirstJsonObject(string s)
    {
        int start = s.IndexOf('{');
        int end = s.LastIndexOf('}');
        if (start >= 0 && end > start) return s.Substring(start, end - start + 1).Trim();
        return s.Trim();
    }
}
