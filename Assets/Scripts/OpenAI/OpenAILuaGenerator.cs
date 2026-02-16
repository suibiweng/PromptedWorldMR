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
    // ============================================================
    // TARGET (SINGLE MODE)
    // ============================================================

    [Header("Target Object (Lua will run here)")]
    [SerializeField] private GameObject target;
    [SerializeField] private LuaBehaviour luaBehaviour;

    [Header("Prompt Inputs")]
    [TextArea(2, 6)]
    public string naturalLanguageIntent;

    // ============================================================
    // RESOURCES
    // ============================================================

    [Header("Resources Paths (no extension)")]
    [SerializeField] private string basePromptResourcePath = "LLM/base_prompt";
    [SerializeField] private string userPromptTemplateResourcePath = "LLM/user_prompt_template";
    [SerializeField] private string apiKeyResourcePath = "Secrets/openai_api_key";

    // ============================================================
    // BACKWARD COMPAT (LuaPromptUI)
    // ============================================================

    [Header("Back-Compat")]
    public string objectDisplayName;
    public bool autoApplyToLuaBehaviour = true;
    public bool callStartAfterApply = true;

    // ============================================================
    // OPENAI
    // ============================================================

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

    // ============================================================
    // GROUP BROADCAST (UNCHANGED)
    // ============================================================

    [Header("Group Broadcast (optional)")]
    [SerializeField] private bool applyToGroup = false;
    [SerializeField] private List<GameObject> groupTargets = new List<GameObject>();

    public void EnableGroupBroadcast(bool on) => applyToGroup = on;

    public void SetGroupTargets(IList<GameObject> targets)
    {
        groupTargets.Clear();
        if (targets == null) return;
        foreach (var go in targets)
            if (go != null) groupTargets.Add(go);
    }

    public IReadOnlyList<GameObject> GroupTargets => groupTargets;

    // ============================================================
    // PLANNER CONTEXT (OPTIONAL)
    // ============================================================

    [NonSerialized] public ScenePlan activeScenePlan = null;

    // ============================================================
    // DEBUG
    // ============================================================

    [NonSerialized] public string lastGeneratedLua = "";
    [NonSerialized] public string lastAssistantMessage = "";
    [NonSerialized] public string lastRawJson = "";
    [NonSerialized] public string lastError = "";

    // ============================================================
    // PUBLIC SINGLE-OBJECT API (UNCHANGED)
    // ============================================================

    public void AssignTarget(GameObject go)
    {
        target = go;
        luaBehaviour = go ? go.GetComponent<LuaBehaviour>() : null;
    }

    public void SetIntent(string intent)
    {
        naturalLanguageIntent = intent;
    }

    public void GenerateLuaNow()
    {
        StartCoroutine(Co_GenerateLua());
    }

    // ============================================================
    // SINGLE-OBJECT GENERATION (UNCHANGED)
    // ============================================================

    private IEnumerator Co_GenerateLua()
    {
        if (target == null || string.IsNullOrWhiteSpace(naturalLanguageIntent))
            yield break;

        if (luaBehaviour == null)
            luaBehaviour = target.GetComponent<LuaBehaviour>();

        string basePrompt = LoadText(basePromptResourcePath);
        string template = LoadText(userPromptTemplateResourcePath);
        string key = LoadText(apiKeyResourcePath);

        if (!string.IsNullOrEmpty(key))
            apiKey = key.Trim();

        if (string.IsNullOrEmpty(basePrompt) ||
            string.IsNullOrEmpty(template) ||
            string.IsNullOrEmpty(apiKey))
            yield break;

        string currentLua = "";
        if (mode == GenerationMode.EditInPlace && luaBehaviour != null)
            currentLua = luaBehaviour.CurrentLua ?? "";

        string userPrompt = BuildUserPrompt(
            template,
            naturalLanguageIntent,
            string.IsNullOrWhiteSpace(objectDisplayName) ? target.name : objectDisplayName,
            currentLua
        );

        var req = new ChatRequest
        {
            model = model,
            temperature = temperature,
            messages = new List<Message>
            {
                new Message { role = "system", content = basePrompt },
                new Message { role = "user", content = userPrompt }
            }
        };

        using var www = new UnityWebRequest(
            "https://api.openai.com/v1/chat/completions", "POST"
        );

        www.uploadHandler = new UploadHandlerRaw(
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(req))
        );
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
            yield break;

        var resp = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);
        if (resp == null || resp.choices.Count == 0)
            yield break;

        string lua = LuaCleaner.Clean(resp.choices[0].message.content);
        lastGeneratedLua = lua;

        if (luaBehaviour == null)
            luaBehaviour = target.AddComponent<LuaBehaviour>();

        luaBehaviour.LoadScript(lua);
        if (callStartAfterApply)
            luaBehaviour.StartRun();

        if (applyToGroup)
        {
            foreach (var go in groupTargets)
            {
                if (go == null || go == target) continue;
                var lb = go.GetComponent<LuaBehaviour>() ?? go.AddComponent<LuaBehaviour>();
                lb.LoadScript(lua);
                if (callStartAfterApply)
                    lb.StartRun();
            }
        }
    }

    // ============================================================
    // ===================== BATCH GENERATION =====================
    // ============================================================

    [Serializable] public class LuaAssignmentPlan
    {
        public List<LuaAssignment> lua_assignments = new();
    }

    [Serializable] public class LuaAssignment
    {
        public string target_id;
        public string lua;
    }

    public LuaAssignmentPlan GenerateBatchForScenePlan(ScenePlan plan)
    {
        if (plan == null)
            return null;

        activeScenePlan = plan;

        // 🔑 LOAD SAME PROMPTS AS SINGLE MODE
        string basePrompt = LoadText(basePromptResourcePath);
        string template = LoadText(userPromptTemplateResourcePath);
        string key = LoadText(apiKeyResourcePath);

        if (!string.IsNullOrEmpty(key))
            apiKey = key.Trim();

        if (string.IsNullOrEmpty(basePrompt) ||
            string.IsNullOrEmpty(template) ||
            string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[OpenAILuaGenerator] Missing batch prompt resources");
            return null;
        }

        string userPrompt = BuildBatchUserPrompt(plan, template);

        Debug.Log("[OpenAILuaGenerator] Batch user prompt:\n" + userPrompt);

        var req = new ChatRequest
        {
            model = model,
            temperature = temperature,
            messages = new List<Message>
            {
                new Message { role = "system", content = basePrompt },
                new Message { role = "user", content = userPrompt }
            }
        };

        using var www = new UnityWebRequest(
            "https://api.openai.com/v1/chat/completions", "POST"
        );

        www.uploadHandler = new UploadHandlerRaw(
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(req))
        );
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", "Bearer " + apiKey);

        www.SendWebRequest();
        while (!www.isDone) { }

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[OpenAILuaGenerator] Batch OpenAI request failed");
            return null;
        }

        var resp = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);
        if (resp == null || resp.choices == null || resp.choices.Count == 0)
            return null;

        string raw = ScenePlanJsonCleaner.Clean(
            resp.choices[0].message.content
        );

        Debug.Log("[OpenAILuaGenerator] RAW BATCH JSON:\n" + raw);

        return JsonUtility.FromJson<LuaAssignmentPlan>(raw);
    }

    // ============================================================
    // ===================== NEW SMART BATCH PROMPT =====================
    // ============================================================

    private string BuildBatchUserPrompt(ScenePlan plan, string template)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You must generate Lua scripts using the EXACT template below.");
        sb.AppendLine("Each script MUST fully satisfy the required lifecycle contract.");
        sb.AppendLine();
        sb.AppendLine("======================================");
        sb.AppendLine("LUA TEMPLATE (USE VERBATIM PER OBJECT)");
        sb.AppendLine("======================================");
        sb.AppendLine(template);
        sb.AppendLine();

        sb.AppendLine("======================================");
        sb.AppendLine("SCENE CONTEXT");
        sb.AppendLine("======================================");
        sb.AppendLine($"Scene type: {plan.scene_type}");
        sb.AppendLine($"Summary: {plan.summary}");
        sb.AppendLine(BuildIoTContext());

        sb.AppendLine();

        sb.AppendLine("Objects in scene:");
        foreach (var o in plan.objects)
        {
            sb.AppendLine(
                $"- id: {o.id}, role: {o.role}, count: {o.count}, interactive: {o.interactive}"
            );
        }

        sb.AppendLine();
        sb.AppendLine("======================================");
        sb.AppendLine("OBJECTS TO GENERATE");
        sb.AppendLine("======================================");

        // Build lookup from planned_behaviors
        Dictionary<string, string> plannedIntentByPrefix = new();
        if (plan.planned_behaviors != null)
        {
            foreach (var pb in plan.planned_behaviors)
            {
                if (string.IsNullOrWhiteSpace(pb.target) || string.IsNullOrWhiteSpace(pb.intent))
                    continue;

                plannedIntentByPrefix[pb.target] = pb.intent;
            }
        }

        foreach (var obj in plan.objects)
        {
            if (!obj.interactive)
                continue;

            string intent = "Design appropriate Mixed Reality behavior for this object.";

            // Try to find a matching planned behavior by prefix
            foreach (var kv in plannedIntentByPrefix)
            {
                if (obj.id.StartsWith(kv.Key))
                {
                    intent = kv.Value;
                    break;
                }
            }

            sb.AppendLine();
            sb.AppendLine("OBJECT:");
            sb.AppendLine($"OBJECT_NAME: {obj.id}");
            sb.AppendLine($"INTENT: {intent}");
            sb.AppendLine($"ROLE: {obj.role}");
            sb.AppendLine($"SCENE_TYPE: {plan.scene_type}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("======================================");
        sb.AppendLine("OUTPUT FORMAT (STRICT)");
        sb.AppendLine("======================================");
        sb.AppendLine(@"
Return ONLY valid JSON in this exact structure:

{
  ""lua_assignments"": [
    {
      ""target_id"": ""OBJECT_NAME"",
      ""lua"": ""<FULL LUA SCRIPT HERE>""
    }
  ]
}

Rules:
- ONE lua_assignments entry per object
- Lua MUST define start/update/on_trigger/on_collision
- Output ONLY JSON
- No markdown
- No explanation
");

        return sb.ToString();
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private string BuildUserPrompt(string template, string intent, string objectName, string currentLua)
    {
        string sceneContext = "";

        if (activeScenePlan != null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ScenePlan context:");
            sb.AppendLine($"Scene type: {activeScenePlan.scene_type}");
            sb.AppendLine("Planned objects:");

            foreach (var o in activeScenePlan.objects)
            {
                sb.AppendLine(
                    $"- id: {o.id}, role: {o.role}, count: {o.count}, interactive: {o.interactive}"
                );
            }

            sceneContext = sb.ToString();
        }

string iotContext = BuildIoTContext();

    return template
    .Replace("{OBJECT_NAME}", objectName)
    .Replace("{INTENT}", intent)
    .Replace("{CURRENT_LUA}", currentLua)
    .Replace("{SCENE_CONTEXT}", sceneContext + iotContext);

    }




    private string BuildIoTContext()
{
    var manager = FindObjectOfType<IOTManager>();
    if (manager == null)
        return "";

    var ids = manager.GetAllDeviceIDs();
    if (ids == null || ids.Count == 0)
        return "";

    var sb = new StringBuilder();

    sb.AppendLine();
    sb.AppendLine("AVAILABLE IOT DEVICES:");

    foreach (var id in ids)
        sb.AppendLine("- " + id);

    sb.AppendLine();
    sb.AppendLine("IoT Usage Rules:");
    sb.AppendLine("- Use iot:On(id) or iot:Off(id)");
    sb.AppendLine("- Only use IDs from this list");
    sb.AppendLine("- Do NOT invent device IDs");
    sb.AppendLine("- Only use IoT if relevant to user intent");
    sb.AppendLine();

    return sb.ToString();
}




    private static string LoadText(string path)
    {
        var ta = Resources.Load<TextAsset>(path);
        return ta ? ta.text : "";
    }

    private static class LuaCleaner
    {
        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            if (raw.StartsWith("```"))
            {
                int a = raw.IndexOf('\n');
                int b = raw.LastIndexOf("```");
                if (a >= 0 && b > a)
                    raw = raw.Substring(a, b - a);
            }
            return raw.Trim();
        }
    }

    // ============================================================
    // DTOs
    // ============================================================

    [Serializable] private class Message { public string role; public string content; }
    [Serializable] private class ChatRequest { public string model; public float temperature; public List<Message> messages; }
    [Serializable] private class Choice { public Message message; }
    [Serializable] private class ChatResponse { public List<Choice> choices; }
}
