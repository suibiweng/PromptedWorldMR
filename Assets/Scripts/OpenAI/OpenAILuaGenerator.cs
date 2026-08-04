using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;
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
    [Tooltip("Replace starts from a fresh script. EditInPlace sends CURRENT_LUA so the model preserves and extends existing behavior.")]
    public GenerationMode mode = GenerationMode.EditInPlace;
    [Tooltip("Include existing Lua scripts from the scene so the model can avoid conflicting controls, especially repeated IoT commands.")]
    [SerializeField] private bool includeSceneLuaContext = true;
    [SerializeField] private int maxSceneLuaScriptsInPrompt = 12;
    [SerializeField] private int maxLuaCharsPerSceneScript = 2400;
    public GenerationMode Mode
    {
        get => mode;
        set => mode = value;
    }

    public void SetGenerationMode(GenerationMode generationMode) => mode = generationMode;
    public void SetEditInPlaceMode(bool editInPlace) => mode = editInPlace ? GenerationMode.EditInPlace : GenerationMode.Replace;

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
    private readonly List<GameObject> selectedContextObjects = new List<GameObject>();

    public void EnableGroupBroadcast(bool on) => applyToGroup = on;

    public void SetGroupTargets(IList<GameObject> targets)
    {
        groupTargets.Clear();
        if (targets == null) return;
        foreach (var go in targets)
        {
            var normalized = NormalizeProgrammableTarget(go);
            if (normalized != null && !groupTargets.Contains(normalized))
                groupTargets.Add(normalized);
        }
    }

    public IReadOnlyList<GameObject> GroupTargets => groupTargets;

    public void SetSelectedContext(GameObject primary, IList<GameObject> selectedObjects)
    {
        selectedContextObjects.Clear();

        if (primary != null)
            selectedContextObjects.Add(NormalizeProgrammableTarget(primary));

        if (selectedObjects == null)
            return;

        foreach (var go in selectedObjects)
        {
            var normalized = NormalizeProgrammableTarget(go);
            if (normalized != null && !selectedContextObjects.Contains(normalized))
                selectedContextObjects.Add(normalized);
        }
    }

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

    private ProgramableObject activePromptLogObject;
    private string activePromptLogId;
    private float activePromptLogStartTime;

    // ============================================================
    // PUBLIC SINGLE-OBJECT API (UNCHANGED)
    // ============================================================

    public void AssignTarget(GameObject go)
    {
        target = NormalizeProgrammableTarget(go);
        luaBehaviour = target ? target.GetComponent<LuaBehaviour>() : null;
    }

    private GameObject NormalizeProgrammableTarget(GameObject go)
    {
        if (go == null)
            return null;

        var programableObject = go.GetComponentInParent<ProgramableObject>();
        return programableObject != null ? programableObject.gameObject : go;
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
        BeginPromptLogIfPossible();

        if (target == null)
        {
            Fail("Missing target for Lua generation.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
        {
            Fail("Empty user prompt.");
            yield break;
        }

        if (luaBehaviour == null)
            luaBehaviour = target.GetComponent<LuaBehaviour>();

        string basePrompt = LoadText(basePromptResourcePath);
        string template = LoadText(userPromptTemplateResourcePath);
        string key = LoadText(apiKeyResourcePath);

        if (!string.IsNullOrEmpty(key))
            apiKey = key.Trim();

        if (string.IsNullOrEmpty(basePrompt) || string.IsNullOrEmpty(template))
        {
            Fail("Missing prompt resources.");
            yield break;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Fail("Missing OpenAI API key.");
            yield break;
        }

        string currentLua = "";
        if (mode == GenerationMode.EditInPlace && luaBehaviour != null)
            currentLua = luaBehaviour.CurrentLua ?? "";

        string runtimeName = target.name;
        string displayLabel = string.IsNullOrWhiteSpace(objectDisplayName) ? runtimeName : objectDisplayName.Trim();

        string userPrompt = BuildUserPrompt(
            template,
            naturalLanguageIntent,
            runtimeName,
            displayLabel,
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

        using var www = CreateChatCompletionRequest(req);
        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
        {
            Fail("OpenAI request failed: " + www.error);
            yield break;
        }

        lastRawJson = www.downloadHandler.text;
        var resp = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);
        if (resp == null || resp.choices == null || resp.choices.Count == 0 || resp.choices[0].message == null)
        {
            Fail("Invalid OpenAI response.");
            yield break;
        }

        lastAssistantMessage = resp.choices[0].message.content ?? "";
        string lua = LuaCleaner.Clean(lastAssistantMessage);
        if (string.IsNullOrWhiteSpace(lua))
        {
            Fail("OpenAI returned empty Lua.");
            yield break;
        }

        if (!ValidateLua(lua, out string validationError))
        {
            Debug.LogWarning("[OpenAILuaGenerator] Rejected Lua:\n" + lua);

            string repairPrompt = BuildRepairPrompt(userPrompt, lua, validationError);
            var repairReq = new ChatRequest
            {
                model = model,
                temperature = 0f,
                messages = new List<Message>
                {
                    new Message { role = "system", content = basePrompt },
                    new Message { role = "user", content = repairPrompt }
                }
            };

            using var repairWww = CreateChatCompletionRequest(repairReq);
            yield return repairWww.SendWebRequest();
            if (repairWww.result != UnityWebRequest.Result.Success)
            {
                Fail("OpenAI repair request failed: " + repairWww.error + "\nOriginal validation error: " + validationError);
                yield break;
            }

            lastRawJson = repairWww.downloadHandler.text;
            var repairResp = JsonUtility.FromJson<ChatResponse>(repairWww.downloadHandler.text);
            if (repairResp == null || repairResp.choices == null || repairResp.choices.Count == 0 || repairResp.choices[0].message == null)
            {
                Fail("Invalid OpenAI repair response.\nOriginal validation error: " + validationError);
                yield break;
            }

            lastAssistantMessage = repairResp.choices[0].message.content ?? "";
            lua = LuaCleaner.Clean(lastAssistantMessage);
            if (string.IsNullOrWhiteSpace(lua))
            {
                Fail("OpenAI repair returned empty Lua.\nOriginal validation error: " + validationError);
                yield break;
            }

            if (!ValidateLua(lua, out string repairValidationError))
            {
                Debug.LogWarning("[OpenAILuaGenerator] Rejected repaired Lua:\n" + lua);
                Fail("Lua repair failed: " + repairValidationError);
                yield break;
            }
        }

        lastGeneratedLua = lua;

        if (!autoApplyToLuaBehaviour)
        {
            PublishReturnMessage(lua);
            CompletePromptLogSuccess(lua);
            yield break;
        }

        if (luaBehaviour == null)
            luaBehaviour = target.AddComponent<LuaBehaviour>();

        luaBehaviour.LoadScript(lua, callStartAfterApply);

        if (applyToGroup)
        {
            foreach (var go in groupTargets)
            {
                if (go == null || go == target) continue;
                var lb = go.GetComponent<LuaBehaviour>() ?? go.AddComponent<LuaBehaviour>();
                lb.LoadScript(lua, callStartAfterApply);
            }
        }

        PublishReturnMessage(lua);
        CompletePromptLogSuccess(lua);
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
        sb.AppendLine(BuildLiveObjectRegistryContext());
        sb.AppendLine(BuildSceneLuaContext(""));

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
            sb.AppendLine($"COLLISION_NAME_PREFIX: {NormalizeCollisionPrefix(obj.id)}");
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

    private string BuildUserPrompt(string template, string intent, string runtimeName, string displayLabel, string currentLua)
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

        string selectedContext = BuildSelectedObjectContext(runtimeName, displayLabel);
        string iotContext = BuildIoTContext();
        string liveObjectRegistry = BuildLiveObjectRegistryContext(runtimeName);
        string sceneLuaContext = BuildSceneLuaContext(currentLua);
        string effectiveIntent = BuildEffectiveIntent(intent);

        return template
            .Replace("{OBJECT_NAME}", runtimeName)
            .Replace("{TARGET_RUNTIME_NAME}", runtimeName)
            .Replace("{TARGET_DISPLAY_LABEL}", displayLabel)
            .Replace("{INTENT}", effectiveIntent)
            .Replace("{CURRENT_LUA}", currentLua)
            .Replace("{SCENE_CONTEXT}", sceneContext + selectedContext + sceneLuaContext)
            .Replace("{IOT_DEVICES}", iotContext)
            .Replace("{LIVE_OBJECT_REGISTRY}", liveObjectRegistry);

    }

    private string BuildEffectiveIntent(string intent)
    {
        string cleanIntent = string.IsNullOrWhiteSpace(intent) ? "" : intent.Trim();
        var globalRuleTarget = target != null ? target.GetComponentInParent<GlobalRuleTarget>() : null;
        if (globalRuleTarget == null || !globalRuleTarget.treatPromptsAsGlobalRules)
            return cleanIntent;

        return "SYSTEM-GENERATED GLOBAL RULE: The selected target is a global environment controller. " +
               "Interpret the user's request as a room/environment-level rule hosted on this controller, not as behavior of the controller mesh itself.\n" +
               "USER REQUEST: " + cleanIntent;
    }

    private string BuildSceneLuaContext(string currentLua)
    {
        if (!includeSceneLuaContext)
            return "";

        var behaviours = FindObjectsOfType<LuaBehaviour>(true);
        if (behaviours == null || behaviours.Length == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("SCENE LUA SCRIPT CONTEXT:");
        sb.AppendLine("Use this to avoid conflicting behavior. If another script already controls the same IoT device or object, do not create a second continuous controller unless the user explicitly wants that.");
        sb.AppendLine("Single-object generation can only replace the PRIMARY TARGET script; other scripts are context unless the UI is explicitly applying to a group.");

        int written = 0;
        var seenRoots = new HashSet<GameObject>();
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            string lua = behaviour.CurrentLua;
            if (string.IsNullOrWhiteSpace(lua))
                continue;

            var root = NormalizeProgrammableTarget(behaviour.gameObject);
            if (root == null)
                root = behaviour.gameObject;

            if (root == null || seenRoots.Contains(root))
                continue;

            seenRoots.Add(root);

            bool isPrimary = target != null && root == target;
            if (isPrimary && string.IsNullOrWhiteSpace(currentLua))
                currentLua = lua;

            if (written >= Mathf.Max(1, maxSceneLuaScriptsInPrompt))
            {
                sb.AppendLine("- Additional scripts omitted to keep prompt size bounded.");
                break;
            }

            AppendSceneLuaEntry(sb, root, behaviour, isPrimary ? currentLua : lua, isPrimary);
            written++;
        }

        if (written == 0)
            sb.AppendLine("- (none)");

        return sb.ToString();
    }

    private void AppendSceneLuaEntry(StringBuilder sb, GameObject root, LuaBehaviour behaviour, string lua, bool isPrimary)
    {
        var po = root != null ? root.GetComponentInParent<ProgramableObject>() : null;
        var iot = root != null ? root.GetComponentInParent<IOTobject>() : null;
        var globalRuleTarget = root != null ? root.GetComponentInParent<GlobalRuleTarget>() : null;

        string name = root != null ? root.name : behaviour.name;
        string label = GetDisplayLabel(root, name);
        string id = iot != null ? iot.DeviceId :
            po != null && !string.IsNullOrWhiteSpace(po.id) ? po.id :
            name;

        sb.AppendLine();
        sb.AppendLine("- Script owner: " + name + (isPrimary ? " (PRIMARY TARGET)" : ""));
        sb.AppendLine("  Internal ID: " + id);
        sb.AppendLine("  Display label: " + label);
        sb.AppendLine("  Real object: " + (po != null && po.isRealObject).ToString().ToLowerInvariant());
        sb.AppendLine("  Global environment controller: " + (globalRuleTarget != null && globalRuleTarget.treatPromptsAsGlobalRules).ToString().ToLowerInvariant());
        sb.AppendLine("  IoT calls detected: " + SummarizeIoTCalls(lua));
        sb.AppendLine("  Lua:");
        sb.AppendLine(IndentLuaForPrompt(TrimLuaForPrompt(lua)));
    }

    private string TrimLuaForPrompt(string lua)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return "(empty)";

        int maxChars = Mathf.Max(400, maxLuaCharsPerSceneScript);
        lua = lua.Trim();
        return lua.Length <= maxChars ? lua : lua.Substring(0, maxChars) + "\n-- ... trimmed for prompt context ...";
    }

    private string IndentLuaForPrompt(string lua)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return "    (empty)";

        return "    " + lua.Replace("\n", "\n    ");
    }

    private string SummarizeIoTCalls(string lua)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return "(none)";

        var calls = new List<string>();
        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(On|Off)\s*\(\s*(['""])(.*?)\2\s*\)", RegexOptions.IgnoreCase))
        {
            string command = match.Groups[1].Value.Equals("On", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF";
            calls.Add($"{IOTManager.NormalizeDeviceId(match.Groups[3].Value)}:{command}");
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*Send\s*\(\s*(['""])(.*?)\1\s*,\s*(['""])(.*?)\3\s*\)", RegexOptions.IgnoreCase))
            calls.Add($"{IOTManager.NormalizeDeviceId(match.Groups[2].Value)}:{IOTManager.NormalizeCommand(match.Groups[4].Value)}");

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(LightBulb|Lightbuld)\s*\(\s*(['""])(.*?)\2\s*\)\s*:\s*(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(", RegexOptions.IgnoreCase))
            calls.Add($"{IOTManager.NormalizeDeviceId(match.Groups[3].Value)}:{IOTManager.NormalizeCommand(match.Groups[4].Value)}");

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(LightBulb|Lightbuld)\s*\(\s*(['""])(.*?)\2\s*\)", RegexOptions.IgnoreCase))
        {
            string call = $"{IOTManager.NormalizeDeviceId(match.Groups[3].Value)}:LIGHTBULB_PROXY";
            if (!calls.Contains(call))
                calls.Add(call);
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(\s*(['""])(.*?)\2\s*,", RegexOptions.IgnoreCase))
            calls.Add($"{IOTManager.NormalizeDeviceId(match.Groups[3].Value)}:{IOTManager.NormalizeCommand(match.Groups[1].Value)}");

        return calls.Count > 0 ? string.Join(", ", calls) : "(none)";
    }




    private string BuildIoTContext()
    {
        var manager = FindObjectOfType<IOTManager>();
        if (manager == null)
            return "";

        var devices = manager.GetAllDeviceInfo();
        if (devices == null || devices.Count == 0)
            return "";

        var sb = new StringBuilder();
        var validIds = new List<string>();
        var colorCapableIds = new List<string>();
        var brightnessCapableIds = new List<string>();
        var onOffOnlyIds = new List<string>();

        foreach (var device in devices)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.id))
                continue;

            validIds.Add(device.id);

            var commands = new HashSet<string>(
                device.supportedCommands ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase
            );

            bool supportsColor = IoTCommandPrefixIsSupported("RGB", commands) ||
                                 IoTCommandPrefixIsSupported("HSV", commands);
            bool supportsBrightness = IoTCommandPrefixIsSupported("BRIGHTNESS", commands);
            bool supportsOnlyOnOff = commands.Count > 0 &&
                                     commands.IsSubsetOf(new HashSet<string>(new[] { "ON", "OFF" }, StringComparer.OrdinalIgnoreCase));

            if (supportsColor)
                colorCapableIds.Add(device.id);
            if (supportsBrightness)
                brightnessCapableIds.Add(device.id);
            if (supportsOnlyOnOff)
                onOffOnlyIds.Add(device.id);
        }

        sb.AppendLine();
        sb.AppendLine("AVAILABLE IOT DEVICES:");
        sb.AppendLine("VALID IOT DEVICE IDS ONLY: " + string.Join(", ", validIds));
        sb.AppendLine("COLOR/RGB/HSV DEVICE IDS: " + FormatIoTIdList(colorCapableIds));
        sb.AppendLine("BRIGHTNESS DEVICE IDS: " + FormatIoTIdList(brightnessCapableIds));
        sb.AppendLine("ON/OFF-ONLY DEVICE IDS: " + FormatIoTIdList(onOffOnlyIds));

        foreach (var device in devices)
        {
            sb.AppendLine();
            sb.AppendLine("- ID: " + device.id);
            sb.AppendLine("  Display name: " + device.displayName);
            sb.AppendLine("  Type: " + device.type);
            sb.AppendLine("  Aliases: " + string.Join(", ", device.aliases ?? new List<string>()));
            sb.AppendLine("  Supported commands: " + string.Join(", ", device.supportedCommands));
            sb.AppendLine("  Current state: " + (device.isOn ? "ON" : "OFF"));
        }

        sb.AppendLine();
        sb.AppendLine("IoT Usage Rules:");
        sb.AppendLine("- IoT commands may control real physical devices.");
        sb.AppendLine("- Use IoT only when the user explicitly requests control of a listed device.");
        sb.AppendLine("- When the user refers to a device by common name, map it to the closest matching display name or ID.");
        sb.AppendLine("- Do not select a device solely because it is the first device in the list.");
        sb.AppendLine("- If no device clearly matches the user's request, generate no IoT command.");
        sb.AppendLine("- Never invent device IDs.");
        sb.AppendLine("- Device IDs are case-insensitive at runtime, but generate the exact ID spelling from VALID IOT DEVICE IDS ONLY.");
        sb.AppendLine("- If the requested ID/name is unclear, choose by capability first: RGB/HSV for color, BRIGHTNESS for darker/brighter, ON/OFF-only for simple power.");
        sb.AppendLine("- Only use supported commands listed for that device.");
        sb.AppendLine("- Lamp/plug names usually mean ON/OFF-only power unless that listed device explicitly supports RGB/HSV or BRIGHTNESS.");
        sb.AppendLine("- Bulb/lightbulb/color-light names mean color/brightness when those commands are listed.");
        sb.AppendLine("- For room/environment color requests, choose a device from COLOR/RGB/HSV DEVICE IDS; do not use an ON/OFF-only lamp/plug.");
        sb.AppendLine("- For room/environment darker/brighter/dim/bright requests, choose a device from BRIGHTNESS DEVICE IDS; do not use an ON/OFF-only lamp/plug.");
        sb.AppendLine("- If both lamp and bulb devices exist, use bulb/color/RGB devices for color/brightness and lamp/plug devices for simple ON/OFF.");
        sb.AppendLine("- For light bulb RGB, prefer iot:LightBulb(\"<id>\"):SetRGB(r,g,b). If separate objects control separate channels, use SetRed, SetGreen, or SetBlue; IOTManager caches the other channels and sends one full RGB URL.");
        sb.AppendLine("- For immediate room/here/environment color or brightness requests, send one command in start(), e.g. iot:Send(\"<color device id>\", \"RGB:0:0:255\") or iot:Send(\"<brightness device id>\", \"BRIGHTNESS:30\").");
        sb.AppendLine("- Never send IoT commands continuously every frame.");
        sb.AppendLine("- Do not place unconditional IoT commands in start() unless the user explicitly requests an immediate initial state.");
        sb.AppendLine();

        return sb.ToString();
    }

    private string FormatIoTIdList(List<string> ids)
    {
        return ids != null && ids.Count > 0 ? string.Join(", ", ids) : "(none)";
    }

    private string BuildIoTDeviceListOnly()
    {
        var manager = FindObjectOfType<IOTManager>();
        if (manager == null)
            return "- (none)";

        var ids = manager.GetAllDeviceIDs();
        if (ids == null || ids.Count == 0)
            return "- (none)";

        var sb = new StringBuilder();
        foreach (var id in ids)
            sb.AppendLine("- " + id);

        return sb.ToString().TrimEnd();
    }

    private string BuildSelectedObjectContext(string runtimeName, string displayLabel)
    {
        var sb = new StringBuilder();
        var primary = target;

        sb.AppendLine();
        sb.AppendLine("PRIMARY TARGET:");
        AppendObjectContext(sb, primary, runtimeName, displayLabel, "- ");

        sb.AppendLine();
        sb.AppendLine("OTHER SELECTED OBJECTS:");

        bool wroteOther = false;
        foreach (var go in selectedContextObjects)
        {
            if (go == null || go == primary)
                continue;

            AppendObjectContext(sb, go, go.name, GetDisplayLabel(go, go.name), "- ");
            wroteOther = true;
        }

        if (!wroteOther)
            sb.AppendLine("- (none)");

        return sb.ToString();
    }

    private void AppendObjectContext(StringBuilder sb, GameObject go, string runtimeName, string displayLabel, string prefix)
    {
        string name = go != null ? go.name : runtimeName;
        string label = string.IsNullOrWhiteSpace(displayLabel) ? GetDisplayLabel(go, name) : displayLabel;
        var po = go != null ? go.GetComponentInParent<ProgramableObject>() : null;
        var iot = go != null ? go.GetComponentInParent<IOTobject>() : null;
        var globalRuleTarget = go != null ? go.GetComponentInParent<GlobalRuleTarget>() : null;
        var lb = go != null ? go.GetComponentInParent<LuaBehaviour>() : null;

        string id = iot != null ? iot.DeviceId :
            po != null && !string.IsNullOrWhiteSpace(po.id) ? po.id :
            name;

        string matchTerm = GetCollisionMatchTerm(id, label, name);
        bool isReal = po != null && po.isRealObject;
        bool isIot = iot != null;
        bool isGlobalRuleTarget = globalRuleTarget != null && globalRuleTarget.treatPromptsAsGlobalRules;
        bool hasButtonProxy = (lb != null && lb.pokeButton != null) ||
                              (go != null && (go.GetComponentInParent<PokeButton>() != null ||
                                              go.GetComponentInChildren<PokeButton>(true) != null));

        sb.AppendLine($"{prefix}Name: {name}");
        sb.AppendLine($"  Internal ID: {id}");
        sb.AppendLine($"  Display label: {label}");
        sb.AppendLine($"  Target role: {(isGlobalRuleTarget ? globalRuleTarget.roleName : "Object")}");
        sb.AppendLine($"  Global environment controller: {isGlobalRuleTarget.ToString().ToLowerInvariant()}");
        sb.AppendLine($"  Real object: {isReal.ToString().ToLowerInvariant()}");
        sb.AppendLine($"  IoT device: {isIot.ToString().ToLowerInvariant()}");
        sb.AppendLine($"  Button proxy available: {hasButtonProxy.ToString().ToLowerInvariant()}");
        if (isGlobalRuleTarget && !string.IsNullOrWhiteSpace(globalRuleTarget.generationHint))
            sb.AppendLine($"  Global rule instruction: {globalRuleTarget.generationHint.Trim()}");
        if (!string.IsNullOrWhiteSpace(matchTerm))
            sb.AppendLine($"  Collision match: col:Matches(\"{matchTerm}\")");
        else
            sb.AppendLine("  Collision match: (no stable friendly name; use only if user explicitly refers to this object)");
    }

    private string GetDisplayLabel(GameObject go, string fallback)
    {
        if (go != null)
        {
            var iot = go.GetComponentInParent<IOTobject>();
            if (iot != null && !string.IsNullOrWhiteSpace(iot.DisplayName))
                return iot.DisplayName;

            var po = go.GetComponentInParent<ProgramableObject>();
            if (po != null && po.TextBox != null && !string.IsNullOrWhiteSpace(po.TextBox.text))
                return po.TextBox.text.Trim();

            if (po != null && po.shape != null && !string.IsNullOrWhiteSpace(po.shape.name) && !IsGenericRuntimeName(po.shape.name))
                return po.shape.name.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Object" : fallback;
    }

    private string GetCollisionMatchTerm(string id, string label, string runtimeName)
    {
        if (!string.IsNullOrWhiteSpace(label) && !IsGenericRuntimeName(label))
            return label.Trim();

        if (!string.IsNullOrWhiteSpace(id) && !IsGenericRuntimeName(id) && !IsOpaqueGeneratedId(id))
            return id.Trim();

        if (!string.IsNullOrWhiteSpace(runtimeName) && !IsGenericRuntimeName(runtimeName))
            return runtimeName.Trim();

        return "";
    }

    private string BuildLiveObjectRegistryContext(string primaryObjectName = null)
    {
        const int maxNames = 128;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var realObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var virtualObjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool TryAddName(HashSet<string> targetSet, string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return false;
            if (IsGenericRuntimeName(n)) return false;
            if (targetSet.Count >= maxNames) return false;
            targetSet.Add(n.Trim());
            return true;
        }

        void AddName(string n)
        {
            TryAddName(names, n);
        }

        void AddRealName(string n)
        {
            if (TryAddName(realObjectNames, n))
                AddName(n);
        }

        void AddVirtualName(string n)
        {
            if (TryAddName(virtualObjectNames, n))
                AddName(n);
        }

        AddName(primaryObjectName);

        if (activeScenePlan != null && activeScenePlan.objects != null)
        {
            foreach (var o in activeScenePlan.objects)
            {
                if (o == null) continue;
                AddName(o.id);
            }
        }

        var worldManager = FindObjectOfType<PromptedWorldManager>();
        if (worldManager != null)
        {
            foreach (var po in worldManager.RealObjects)
            {
                if (po == null) continue;
                AddRealName(po.gameObject != null ? po.gameObject.name : null);
                AddRealName(po.TextBox != null ? po.TextBox.text : null);
                AddRealName(po.shape != null ? po.shape.name : null);
            }

            foreach (var po in worldManager.VirtualObjects)
            {
                if (po == null) continue;
                AddVirtualName(po.gameObject != null ? po.gameObject.name : null);
                AddVirtualName(po.TextBox != null ? po.TextBox.text : null);
                AddVirtualName(po.shape != null ? po.shape.name : null);
            }
        }
        else
        {
            foreach (var po in FindObjectsByType<ProgramableObject>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (po == null) continue;
                AddName(po.gameObject != null ? po.gameObject.name : null);
                AddName(po.TextBox != null ? po.TextBox.text : null);
                AddName(po.shape != null ? po.shape.name : null);
                if (po.isRealObject)
                {
                    AddRealName(po.gameObject != null ? po.gameObject.name : null);
                    AddRealName(po.TextBox != null ? po.TextBox.text : null);
                    AddRealName(po.shape != null ? po.shape.name : null);
                }
                else
                {
                    AddVirtualName(po.gameObject != null ? po.gameObject.name : null);
                    AddVirtualName(po.TextBox != null ? po.TextBox.text : null);
                    AddVirtualName(po.shape != null ? po.shape.name : null);
                }
            }
        }

        if (names.Count == 0)
            return "";

        var sortedNames = new List<string>(names);
        sortedNames.Sort(StringComparer.OrdinalIgnoreCase);
        var sortedRealNames = new List<string>(realObjectNames);
        sortedRealNames.Sort(StringComparer.OrdinalIgnoreCase);
        var sortedVirtualNames = new List<string>(virtualObjectNames);
        sortedVirtualNames.Sort(StringComparer.OrdinalIgnoreCase);

        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in sortedNames)
            prefixes.Add(NormalizeCollisionPrefix(n));

        var sortedPrefixes = new List<string>(prefixes);
        sortedPrefixes.Sort(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("LIVE OBJECT REGISTRY (scene lookup and collision-relevant names):");
        sb.AppendLine("REAL OBJECTS (MR room/furniture/physical anchors; use scene:FindRealObject when user names these):");
        if (sortedRealNames.Count == 0)
            sb.AppendLine("- (none)");
        else
            foreach (var n in sortedRealNames)
                sb.AppendLine("- " + n);

        sb.AppendLine();
        sb.AppendLine("VIRTUAL OBJECTS (spawned/generated objects; use scene:FindVirtualObject when user names these):");
        if (sortedVirtualNames.Count == 0)
            sb.AppendLine("- (none)");
        else
            foreach (var n in sortedVirtualNames)
                sb.AppendLine("- " + n);

        sb.AppendLine();
        sb.AppendLine("ALL LOOKUP NAMES:");
        foreach (var n in sortedNames)
            sb.AppendLine("- " + n);

        sb.AppendLine();
        sb.AppendLine("Scene lookup rule:");
        sb.AppendLine("- If a named target appears under REAL OBJECTS, treat it as room furniture/physical anchor and use scene:FindRealObject(\"name\").");
        sb.AppendLine("- If a named target appears under VIRTUAL OBJECTS, treat it as generated virtual content and use scene:FindVirtualObject(\"name\").");
        sb.AppendLine("- If unsure, use scene:FindObject(\"name\") and nil-check.");

        sb.AppendLine();
        sb.AppendLine("COLLISION MATCH TERMS (prefer col:Matches(\"term\")):");
        foreach (var p in sortedPrefixes)
            sb.AppendLine("- " + p);
        sb.AppendLine();
        sb.AppendLine("Collision naming rule:");
        sb.AppendLine("- Prefer col:Matches(\"display label or id\") for collision checks.");
        sb.AppendLine("- Runtime names can be generic, such as ProgramableObject_Virtual.");
        sb.AppendLine("- Use col:GetRootName() and prefixes only as a fallback.");
        sb.AppendLine();

        return sb.ToString();
    }

    private string NormalizeCollisionPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "OBJECT";

        string s = name.Trim().ToUpperInvariant().Replace(' ', '_');

        if (s.EndsWith("(CLONE)", StringComparison.Ordinal))
            s = s.Substring(0, s.Length - "(CLONE)".Length).TrimEnd('_');

        int i = s.Length - 1;
        while (i >= 0 && char.IsDigit(s[i]))
            i--;
        if (i >= 0 && i < s.Length - 1 && s[i] == '_')
            s = s.Substring(0, i);

        while (s.Contains("__"))
            s = s.Replace("__", "_");

        return string.IsNullOrWhiteSpace(s) ? "OBJECT" : s;
    }

    private bool IsGenericRuntimeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string normalized = name.Trim().Replace(' ', '_').ToUpperInvariant();
        return normalized == "PROGRAMABLEOBJECT" ||
               normalized == "PROGRAMMABLEOBJECT" ||
               normalized.StartsWith("PROGRAMABLEOBJECT_") ||
               normalized.StartsWith("PROGRAMMABLEOBJECT_");
    }

    private bool IsOpaqueGeneratedId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string normalized = name.Trim();
        return normalized.Length >= 16 &&
               Regex.IsMatch(normalized, @"^[A-Fa-f0-9]+$") &&
               Regex.IsMatch(normalized, @"\d");
    }

    private bool ValidateLua(string lua, out string error)
    {
        error = "";

        try
        {
            var script = new Script(CoreModules.Preset_Default);
            script.LoadString(lua);
        }
        catch (SyntaxErrorException ex)
        {
            error = "Lua syntax failure: " + ex.DecoratedMessage;
            return false;
        }

        string[] requiredFunctions =
        {
            "start",
            "update",
            "on_trigger",
            "on_collision"
        };

        foreach (var fn in requiredFunctions)
        {
            if (!HasGlobalLifecycleFunction(lua, fn))
            {
                error = $"Missing lifecycle function: {fn}. Generated Lua must be a full script with start/update/on_trigger/on_collision. Excerpt: {BuildLuaExcerpt(lua)}";
                return false;
            }
        }

        string[] forbiddenApis =
        {
            "GetComponent",
            "AddComponent",
            "GetComponentInChildren",
            "GetComponentInParent",
            "GameObject.Find",
            "FindObjectOfType",
            "FindAnyObjectByType",
            "UnityEngine",
            "Time.deltaTime"
        };

        foreach (var api in forbiddenApis)
        {
            if (lua.IndexOf(api, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "Forbidden API usage: " + api;
                return false;
            }
        }

        var unsafeAssignment = Regex.Match(
            lua,
            @"\bself\.(rigidbody|audio|audioSource|particles|particleSystem|animator|button|poke|gameObject)\.[A-Za-z_]\w*\s*=",
            RegexOptions.IgnoreCase
        );
        if (unsafeAssignment.Success)
        {
            error = "Forbidden direct userdata field assignment: " + unsafeAssignment.Value.Trim();
            return false;
        }

        if (!ValidateHandTouchIntent(lua, out error))
            return false;

        if (!ValidateContinuousSizeIntent(lua, out error))
            return false;

        if (!ValidateClapIntent(lua, out error))
            return false;

        if (!ValidateNamedObjectProximityIntent(lua, out error))
            return false;

        if (!ValidateObjectMetricControlIntent(lua, out error))
            return false;

        if (!ValidateCollisionMatches(lua, out error))
            return false;

        if (!ValidateRoomLightingIntent(lua, out error))
            return false;

        if (!ValidateIoTCalls(lua, out error))
            return false;

        return true;
    }

    private bool ValidateRoomLightingIntent(string lua, out string error)
    {
        error = "";

        if (!UserIntentMeansRoomLightingControl())
            return true;

        bool wantsColor = UserIntentMeansRoomColorControl();
        bool wantsBrightness = UserIntentMeansRoomBrightnessControl();

        if (!TryFindPreferredRoomLightDevice(out string deviceId, out bool supportsRgbOrHsv, out bool supportsBrightness))
            return true;

        if (wantsColor && supportsRgbOrHsv && !LuaSendsIoTCommandToDevice(lua, deviceId, "RGB", "HSV"))
        {
            error = $"The user asked for room/environment color, so control the RGB/HSV bulb device {deviceId} with iot:LightBulb(\"{deviceId}\"):SetRGB(r,g,b), SetRed/SetGreen/SetBlue, or iot:Send(\"{deviceId}\", \"RGB:r:g:b\"). Do not only change the selected object's material color.";
            return false;
        }

        if (wantsBrightness && supportsBrightness && !LuaSendsIoTCommandToDevice(lua, deviceId, "BRIGHTNESS"))
        {
            error = $"The user asked for room/environment brightness, so control the brightness-capable bulb device {deviceId} with iot:Send(\"{deviceId}\", \"BRIGHTNESS:value\"). Do not only change the selected object's material color.";
            return false;
        }

        return true;
    }

    private bool ValidateNamedObjectProximityIntent(string lua, out string error)
    {
        error = "";

        if (!UserIntentMeansNamedObjectProximity())
            return true;

        bool usesSceneLookup =
            Regex.IsMatch(lua, @"\b(scene|world)\s*:\s*Find(RealObject|VirtualObject|Object)?\s*\(", RegexOptions.IgnoreCase);

        bool usesManualHeadDistance =
            lua.IndexOf(":GetHeadPosition", StringComparison.OrdinalIgnoreCase) >= 0 &&
            lua.IndexOf(":GetTransformProxy", StringComparison.OrdinalIgnoreCase) >= 0 &&
            Regex.IsMatch(lua, @"\b(dx|dy|dz|math\.sqrt|distance)\b", RegexOptions.IgnoreCase);

        bool incorrectlyUsesThisObject =
            lua.IndexOf(":IsHeadCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":MapHeadDistanceToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsUserClose", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":GetUserHeadDistance", StringComparison.OrdinalIgnoreCase) >= 0;

        bool usesCollision =
            Regex.IsMatch(lua, @"\bcol\s*:", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(lua, @"function\s+on_collision\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(iot|self|scene|world|user)\b", RegexOptions.IgnoreCase);

        if (!usesSceneLookup || !usesManualHeadDistance || incorrectlyUsesThisObject || usesCollision)
        {
            error = "The user asked about being close/near a named scene object, so use LIVE_OBJECT_REGISTRY plus scene:FindRealObject/FindVirtualObject/FindObject, user:GetHeadPosition(), the target transform position, and manual distance math in update(). Do not use this-object proximity APIs or collision.";
            return false;
        }

        return true;
    }

    private bool ValidateObjectMetricControlIntent(string lua, out string error)
    {
        error = "";

        if (!UserIntentMeansObjectMetricControl())
            return true;

        bool usesUpdate =
            Regex.IsMatch(lua, @"function\s+update\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(position|localScale|GetTransformProxy|SetColor|iot|VOLUME|math\.sqrt|distance)\b", RegexOptions.IgnoreCase);

        bool usesMetric =
            lua.IndexOf("localScale", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (lua.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0 &&
             Regex.IsMatch(lua, @"\b(dx|dy|dz|math\.sqrt|distance)\b", RegexOptions.IgnoreCase));

        bool usesCollision =
            Regex.IsMatch(lua, @"\bcol\s*:", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(lua, @"function\s+on_collision\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(iot|self|scene|world|transform|SetColor)\b", RegexOptions.IgnoreCase);

        bool incorrectlyUsesUserDistance =
            !UserIntentMentionsUserBody() &&
            (lua.IndexOf(":MapHeadDistanceToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
             lua.IndexOf(":MapNearestHandDistanceToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
             lua.IndexOf(":MapHandDistance", StringComparison.OrdinalIgnoreCase) >= 0 ||
             lua.IndexOf(":GetHandDistance", StringComparison.OrdinalIgnoreCase) >= 0 ||
             lua.IndexOf(":GetHeadDistanceToThisObject", StringComparison.OrdinalIgnoreCase) >= 0);

        if (!usesUpdate || !usesMetric || usesCollision || incorrectlyUsesUserDistance)
        {
            error = "The user asked for object size/distance to control behavior, so use update(), scene lookup as needed, TransformProxy.position/localScale, and manual Lua math. Do not use collision or user hand/head distance APIs unless the user mentions the user's body.";
            return false;
        }

        return true;
    }

    private bool ValidateClapIntent(string lua, out string error)
    {
        error = "";

        if (!UserIntentMeansClapControl())
            return true;

        bool usesTwoHandDistance =
            lua.IndexOf(":GetHandDistance", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":MapHandDistance", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsHandsClose", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":AreHandsClose", StringComparison.OrdinalIgnoreCase) >= 0;

        bool usesWrongTouchOrCollision =
            Regex.IsMatch(lua, @"\bcol\s*:", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(lua, @"function\s+on_collision\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(iot|self|scene|world|user)\b", RegexOptions.IgnoreCase) ||
            lua.IndexOf(":IsAnyHandCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsLeftHandCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsRightHandCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0;

        bool usesUpdate = Regex.IsMatch(lua, @"function\s+update\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(user|iot)\b", RegexOptions.IgnoreCase);

        if (!usesTwoHandDistance || usesWrongTouchOrCollision || !usesUpdate)
        {
            error = "The user asked for a clap, so this must use two-hand distance in update() such as user:IsHandsClose(threshold) or user:GetHandDistance(), with edge detection. Do not use object touch, proximity-to-object, on_collision, or col:Matches.";
            return false;
        }

        return true;
    }

    private bool ValidateContinuousSizeIntent(string lua, out string error)
    {
        error = "";

        if (!UserIntentMeansContinuousSizeControl())
            return true;

        bool usesCollisionAction =
            Regex.IsMatch(lua, @"function\s+on_collision\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(iot|self|dotween|scene|world|rigidbody|transform)\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(lua, @"\bcol\s*:", RegexOptions.IgnoreCase);

        if (usesCollisionAction)
        {
            error = "The user asked for breathing/size-following behavior, so this must be continuous update(self, deltaTime) logic based on scale/size thresholds, not on_collision/col:Matches.";
            return false;
        }

        bool usesUpdate = Regex.IsMatch(lua, @"function\s+update\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(localScale|ScaleTo|iot|transform)\b", RegexOptions.IgnoreCase);
        if (!usesUpdate)
        {
            error = "The user asked for breathing/size-following behavior, so update(self, deltaTime) must animate/read size and trigger IoT from size thresholds.";
            return false;
        }

        return true;
    }

    private bool ValidateHandTouchIntent(string lua, out string error)
    {
        error = "";

        if (!UserIntentMeansHandTouch())
            return true;

        bool usesHandTouchApi =
            lua.IndexOf(":IsTouching", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":GetIsTouching", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsAnyHandCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsLeftHandCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":IsRightHandCloseToThisObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
            lua.IndexOf(":GetNearestHandDistanceToThisObject", StringComparison.OrdinalIgnoreCase) >= 0;

        bool usesCollisionApi =
            Regex.IsMatch(lua, @"\bcol\s*:", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(lua, @"function\s+on_collision\s*\([^)]*\)\s*(?!end\b)[\s\S]*?\b(iot|self|dotween|particles|audio)\b", RegexOptions.IgnoreCase);

        if (!usesHandTouchApi || usesCollisionApi)
        {
            error = "The user said they touch the object, so this must use hand/user touch APIs in update() such as self.programable:IsTouching() or user:IsAnyHandCloseToThisObject(...), not on_collision/col:Matches.";
            return false;
        }

        return true;
    }

    private bool ValidateCollisionMatches(string lua, out string error)
    {
        error = "";
        bool anyCollisionIntent = UserIntentMeansAnyCollision();
        string namedCollisionTarget = ExtractNamedCollisionTarget();
        bool hasCollisionMatch = false;

        foreach (Match match in Regex.Matches(lua, @"col\s*:\s*Matches\s*\(\s*(['""])(.*?)\1\s*\)", RegexOptions.IgnoreCase))
        {
            hasCollisionMatch = true;
            string term = match.Groups[2].Value;

            if (anyCollisionIntent)
            {
                error = $"Invalid collision condition col:Matches(\"{term}\"). The user asked for any/something collision, so on_collision must run the action without filtering by object.";
                return false;
            }

            if (IsGenericRuntimeName(term))
            {
                error = $"Invalid collision target '{term}'. ProgramableObject_* names are generic runtime wrapper names; use a display label/object ID, or omit col:Matches when the user means any collision.";
                return false;
            }

            if (IsOpaqueGeneratedId(term))
            {
                error = $"Invalid collision target '{term}'. This looks like an internal generated object ID; use a human term such as Sphere/Cube or the object's visible label.";
                return false;
            }
        }

        if (!string.IsNullOrEmpty(namedCollisionTarget) && !hasCollisionMatch)
        {
            error = $"Missing collision target condition. The user asked for collision with '{namedCollisionTarget}', so on_collision must check col:Matches(\"{namedCollisionTarget}\") or another stable label/id for that object.";
            return false;
        }

        return true;
    }

    private bool UserIntentMeansAnyCollision()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();
        return intent.Contains("hits something") ||
               intent.Contains("hit something") ||
               intent.Contains("collides with something") ||
               intent.Contains("collide with something") ||
               intent.Contains("hits anything") ||
               intent.Contains("hit anything") ||
               intent.Contains("collides with anything") ||
               intent.Contains("collide with anything") ||
               intent.Contains("hits any object") ||
               intent.Contains("hit any object") ||
               intent.Contains("collides with any object") ||
               intent.Contains("collide with any object");
    }

    private bool UserIntentMeansContinuousSizeControl()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();

        if (intent.Contains("collision") ||
            intent.Contains("collide") ||
            intent.Contains("collides") ||
            intent.Contains("hit ") ||
            intent.Contains("hits ") ||
            intent.Contains("bump"))
            return false;

        bool sizeOrBreathing =
            Regex.IsMatch(intent, @"\b(breathe|breath|breathing|brething|pulse|pulsing|scale|size|grow|grows|growing|shrink|shrinks|shrinking|expand|expands|expanding)\b");

        bool continuousControl =
            Regex.IsMatch(intent, @"\b(follow|follows|following|based on|according to|when it grows|when it shrinks|as it grows|as it shrinks|turn on and off)\b");

        bool deviceControl =
            Regex.IsMatch(intent, @"\b(light|lamp|iot|device|tv|speaker)\b");

        return sizeOrBreathing && (continuousControl || deviceControl);
    }

    private bool UserIntentMeansObjectMetricControl()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();

        if (intent.Contains("collision") ||
            intent.Contains("collide") ||
            intent.Contains("collides") ||
            intent.Contains("hit ") ||
            intent.Contains("hits ") ||
            intent.Contains("bump") ||
            UserIntentMeansClapControl())
            return false;

        bool distanceMetric =
            Regex.IsMatch(intent, @"\b(distance|nearer|closer|farther|further|between)\b") ||
            Regex.IsMatch(intent, @"\b(close|near|far)\s+(to|from|between)\b");

        bool sizeMetric =
            Regex.IsMatch(intent, @"\b(size|scale|local\s*scale|bigger|smaller|growing|shrinking|expanding)\b");

        bool metricRelationship =
            Regex.IsMatch(intent, @"\b(control|controls|drive|drives|map|maps|trigger|triggers|turn|turns|follow|follows|following|based on|according to|when|if|as)\b");

        bool affectedOutput =
            Regex.IsMatch(intent, @"\b(color|colour|volume|speaker|tv|light|lamp|iot|device|brightness|animation|speed|red|green|blue)\b");

        return affectedOutput && metricRelationship && (distanceMetric || sizeMetric);
    }

    private bool UserIntentMeansRoomLightingControl()
    {
        return UserIntentMeansRoomColorControl() || UserIntentMeansRoomBrightnessControl();
    }

    private bool UserIntentMeansRoomColorControl()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();
        bool colorRequest =
            Regex.IsMatch(intent, @"\b(color|colour|rgb|hsv|red|green|blue|bule|purple|pink|yellow|orange|white|warm|cool)\b");

        return colorRequest && UserIntentTargetsRoomOrBulb(intent);
    }

    private bool UserIntentMeansRoomBrightnessControl()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();
        bool brightnessRequest =
            Regex.IsMatch(intent, @"\b(brightness|bright|brighter|dark|darker|dim|dimmer|light level)\b");

        return brightnessRequest && UserIntentTargetsRoomOrBulb(intent);
    }

    private bool UserIntentTargetsRoomOrBulb(string intent)
    {
        if (TargetIsGlobalRuleTarget())
            return true;

        return Regex.IsMatch(intent, @"\b(room|environment|global|here|space|around|ambient|bulb|lightbulb|light bulb|bulb01|buld01)\b");
    }

    private bool TargetIsGlobalRuleTarget()
    {
        var globalRuleTarget = target != null ? target.GetComponentInParent<GlobalRuleTarget>() : null;
        return globalRuleTarget != null && globalRuleTarget.treatPromptsAsGlobalRules;
    }

    private bool TryFindPreferredRoomLightDevice(
        out string deviceId,
        out bool supportsRgbOrHsv,
        out bool supportsBrightness)
    {
        deviceId = "";
        supportsRgbOrHsv = false;
        supportsBrightness = false;

        var manager = FindObjectOfType<IOTManager>();
        var devices = manager != null ? manager.GetAllDeviceInfo() : new List<IoTDeviceInfo>();
        IoTDeviceInfo best = null;
        int bestScore = int.MinValue;

        foreach (var device in devices)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.id))
                continue;

            var commands = new HashSet<string>(
                device.supportedCommands ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase
            );

            bool hasRgbOrHsv = IoTCommandPrefixIsSupported("RGB", commands) ||
                               IoTCommandPrefixIsSupported("HSV", commands);
            bool hasBrightness = IoTCommandPrefixIsSupported("BRIGHTNESS", commands);

            if (!hasRgbOrHsv && !hasBrightness)
                continue;

            int score = 0;
            if (hasRgbOrHsv) score += 10;
            if (hasBrightness) score += 10;
            if (string.Equals(device.type, IOTtype.LightBulb.ToString(), StringComparison.OrdinalIgnoreCase)) score += 20;
            if (device.id.IndexOf("BULB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                device.id.IndexOf("BULD", StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
            if ((device.displayName ?? "").IndexOf("bulb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (device.displayName ?? "").IndexOf("buld", StringComparison.OrdinalIgnoreCase) >= 0) score += 5;

            if (score > bestScore)
            {
                best = device;
                bestScore = score;
                supportsRgbOrHsv = hasRgbOrHsv;
                supportsBrightness = hasBrightness;
            }
        }

        if (best == null)
            return false;

        deviceId = best.id;
        return true;
    }

    private bool LuaSendsIoTCommandToDevice(string lua, string expectedDeviceId, params string[] allowedPrefixes)
    {
        if (string.IsNullOrWhiteSpace(lua) || string.IsNullOrWhiteSpace(expectedDeviceId))
            return false;

        var manager = FindObjectOfType<IOTManager>();

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*Send\s*\(\s*(['""])(.*?)\1\s*,\s*(['""])(.*?)\3\s*\)", RegexOptions.IgnoreCase))
        {
            string sentDevice = manager != null ? manager.ResolveDeviceId(match.Groups[2].Value) : IOTManager.NormalizeDeviceId(match.Groups[2].Value);
            if (!string.Equals(sentDevice, expectedDeviceId, StringComparison.OrdinalIgnoreCase))
                continue;

            string command = IOTManager.NormalizeCommand(match.Groups[4].Value);
            foreach (var prefix in allowedPrefixes)
            {
                if (command.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(command, prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(LightBulb|Lightbuld)\s*\(\s*(['""])(.*?)\2\s*\)\s*:\s*(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(", RegexOptions.IgnoreCase))
        {
            string sentDevice = manager != null ? manager.ResolveDeviceId(match.Groups[3].Value) : IOTManager.NormalizeDeviceId(match.Groups[3].Value);
            if (!string.Equals(sentDevice, expectedDeviceId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var prefix in allowedPrefixes)
                if (string.Equals(prefix, "RGB", StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(LightBulb|Lightbuld)\s*\(\s*(['""])(.*?)\2\s*\)", RegexOptions.IgnoreCase))
        {
            string sentDevice = manager != null ? manager.ResolveDeviceId(match.Groups[3].Value) : IOTManager.NormalizeDeviceId(match.Groups[3].Value);
            if (!string.Equals(sentDevice, expectedDeviceId, StringComparison.OrdinalIgnoreCase))
                continue;

            bool hasBulbChannelSetter = Regex.IsMatch(lua, @"\b(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(", RegexOptions.IgnoreCase);
            if (!hasBulbChannelSetter)
                continue;

            foreach (var prefix in allowedPrefixes)
                if (string.Equals(prefix, "RGB", StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(\s*(['""])(.*?)\2\s*,", RegexOptions.IgnoreCase))
        {
            string sentDevice = manager != null ? manager.ResolveDeviceId(match.Groups[3].Value) : IOTManager.NormalizeDeviceId(match.Groups[3].Value);
            if (!string.Equals(sentDevice, expectedDeviceId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var prefix in allowedPrefixes)
                if (string.Equals(prefix, "RGB", StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        return false;
    }

    private bool UserIntentMentionsUserBody()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();
        return Regex.IsMatch(intent, @"\b(user|head|hand|hands|finger|fingers|body|clap|clapping)\b") ||
               Regex.IsMatch(intent, @"\bmy\s+(head|hand|hands|finger|fingers|body)\b");
    }

    private bool UserIntentMeansClapControl()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();

        if (intent.Contains("collision") ||
            intent.Contains("collide") ||
            intent.Contains("collides") ||
            intent.Contains("hit ") ||
            intent.Contains("hits ") ||
            intent.Contains("bump"))
            return false;

        return Regex.IsMatch(intent, @"\b(clap|claps|clapped|clapping)\b") ||
               Regex.IsMatch(intent, @"\b(hands?\s+together|two\s+hands?\s+together|bring\s+(my\s+)?hands?\s+together)\b") ||
               Regex.IsMatch(intent, @"\b(hands?|two\s+hands?)\s+(are\s+)?(close|near)\s+(to\s+)?(each\s+other|together)\b") ||
               Regex.IsMatch(intent, @"\b(when|if)?\s*(my\s+)?hands?\s+(get|gets|become|becomes|are|is)\s+(close|near)\b") ||
               Regex.IsMatch(intent, @"\bbring\s+(my\s+)?hands?\s+(close|near)\b");
    }

    private bool UserIntentMeansNamedObjectProximity()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();

        if (intent.Contains("collision") ||
            intent.Contains("collide") ||
            intent.Contains("collides") ||
            intent.Contains("hit ") ||
            intent.Contains("hits ") ||
            intent.Contains("bump"))
            return false;

        var match = Regex.Match(
            intent,
            @"\b(?:i\s+am\s+|i'?m\s+|i\s+|user\s+is\s+|when\s+i\s+am\s+|when\s+i'?m\s+|when\s+i\s+|when\s+user\s+is\s+)?(?:close|near|approach|approaching)\s+(?:to\s+)?(?:the\s+)?([a-z0-9 _-]+?)(?:,|\.|$|\s+(?:then|and|turn|make|set|open|close|play|stop))",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return false;

        string targetName = match.Groups[1].Value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        return targetName != "this" &&
               targetName != "it" &&
               targetName != "object" &&
               targetName != "this object" &&
               targetName != "the object";
    }

    private bool UserIntentMeansHandTouch()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return false;

        string intent = naturalLanguageIntent.Trim().ToLowerInvariant();

        if (UserIntentMeansButtonInteraction(intent))
            return false;

        if (TargetHasButtonProxy() && Regex.IsMatch(intent, @"\b(touch|touches|touched|touching)\b", RegexOptions.IgnoreCase))
            return false;

        if (intent.Contains("collision") ||
            intent.Contains("collide") ||
            intent.Contains("collides") ||
            intent.Contains("hit ") ||
            intent.Contains("hits ") ||
            intent.Contains("bump"))
            return false;

        return Regex.IsMatch(intent, @"\b(i|my hand|my hands|hand|hands|finger|fingers)\s+(touch|touches|touched|touching)\b") ||
               Regex.IsMatch(intent, @"\bwhen\s+i\s+touch\b") ||
               Regex.IsMatch(intent, @"\bif\s+i\s+touch\b") ||
               Regex.IsMatch(intent, @"\bi\s+touch\s+(it|this|this object|the object)\b");
    }

    private bool UserIntentMeansButtonInteraction(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return false;

        return Regex.IsMatch(
            intent,
            @"\b(button|buttons|poke|poked|poking|press|pressed|pressing|click|clicked|clicking|tap|tapped|tapping)\b",
            RegexOptions.IgnoreCase);
    }

    private bool TargetHasButtonProxy()
    {
        if (target == null)
            return false;

        var lb = luaBehaviour != null ? luaBehaviour : target.GetComponentInParent<LuaBehaviour>();
        if (lb != null && lb.pokeButton != null)
            return true;

        return target.GetComponentInParent<PokeButton>() != null ||
               target.GetComponentInChildren<PokeButton>(true) != null;
    }

    private string ExtractNamedCollisionTarget()
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageIntent))
            return "";

        if (UserIntentMeansAnyCollision())
            return "";

        string intent = naturalLanguageIntent.Trim();
        string[] patterns =
        {
            @"\b(?:hits|hit|hitting)\s+(?:the\s+)?([A-Za-z0-9 _-]+?)(?:,|\.|$|\s+(?:turn|then|and|to|make|set|play|stop))",
            @"\b(?:collides|collide|colliding)\s+with\s+(?:the\s+)?([A-Za-z0-9 _-]+?)(?:,|\.|$|\s+(?:turn|then|and|to|make|set|play|stop))"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(intent, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            string targetName = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(targetName))
                continue;

            string lower = targetName.ToLowerInvariant();
            if (lower == "something" || lower == "anything" || lower == "any object")
                return "";

            return targetName;
        }

        return "";
    }

    private bool HasGlobalLifecycleFunction(string lua, string functionName)
    {
        string source = StripLuaComments(lua);
        string escaped = Regex.Escape(functionName);

        return Regex.IsMatch(source, $@"(^|[\r\n])\s*function\s+{escaped}\s*\(", RegexOptions.Multiline) ||
               Regex.IsMatch(source, $@"(^|[\r\n])\s*{escaped}\s*=\s*function\s*\(", RegexOptions.Multiline);
    }

    private string StripLuaComments(string lua)
    {
        if (string.IsNullOrEmpty(lua))
            return "";

        string withoutBlocks = Regex.Replace(lua, @"--\[\[[\s\S]*?\]\]", "");
        return Regex.Replace(withoutBlocks, @"--.*?$", "", RegexOptions.Multiline);
    }

    private string BuildLuaExcerpt(string lua)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return "(empty)";

        string compact = Regex.Replace(lua.Trim(), @"\s+", " ");
        return compact.Length <= 240 ? compact : compact.Substring(0, 240) + "...";
    }

    private bool ValidateIoTCalls(string lua, out string error)
    {
        error = "";

        var manager = FindObjectOfType<IOTManager>();
        var devices = manager != null ? manager.GetAllDeviceInfo() : new List<IoTDeviceInfo>();
        var supportedById = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var aliasesToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.id))
                continue;

            supportedById[device.id] = new HashSet<string>(
                device.supportedCommands ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase
            );

            AddIoTAlias(aliasesToId, device.id, device.id);
            AddIoTAlias(aliasesToId, device.displayName, device.id);
            if (device.aliases != null)
                foreach (var alias in device.aliases)
                    AddIoTAlias(aliasesToId, alias, device.id);
        }

        var literalCallSpans = new List<Vector2Int>();

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(On|Off)\s*\(\s*(['""])(.*?)\2\s*\)", RegexOptions.IgnoreCase))
        {
            literalCallSpans.Add(new Vector2Int(match.Index, match.Length));
            string method = match.Groups[1].Value;
            string id = ResolveIoTReference(match.Groups[3].Value, aliasesToId);
            string command = method.Equals("On", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF";

            if (!ValidateIoTCommand(id, command, supportedById, out error))
                return false;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*Send\s*\(\s*(['""])(.*?)\1\s*,\s*(['""])(.*?)\3\s*\)", RegexOptions.IgnoreCase))
        {
            literalCallSpans.Add(new Vector2Int(match.Index, match.Length));
            string id = ResolveIoTReference(match.Groups[2].Value, aliasesToId);
            string command = IOTManager.NormalizeCommand(match.Groups[4].Value);

            if (!ValidateIoTCommand(id, command, supportedById, out error))
                return false;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*Send\s*\(\s*(['""])(.*?)\1\s*,\s*([^\)]*)\)", RegexOptions.IgnoreCase))
        {
            if (SpanAlreadyRecorded(match.Index, literalCallSpans))
                continue;

            string id = ResolveIoTReference(match.Groups[2].Value, aliasesToId);
            string commandExpression = match.Groups[3].Value;

            if (!ValidateDynamicIoTCommandExpression(id, commandExpression, supportedById, out error))
                return false;

            literalCallSpans.Add(new Vector2Int(match.Index, match.Length));
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(LightBulb|Lightbuld)\s*\(\s*(['""])(.*?)\2\s*\)\s*:\s*(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(", RegexOptions.IgnoreCase))
        {
            literalCallSpans.Add(new Vector2Int(match.Index, match.Length));
            string id = ResolveIoTReference(match.Groups[3].Value, aliasesToId);

            if (!ValidateIoTCommand(id, "RGB:0:0:0", supportedById, out error))
                return false;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(LightBulb|Lightbuld)\s*\(\s*(['""])(.*?)\2\s*\)", RegexOptions.IgnoreCase))
        {
            if (SpanAlreadyRecorded(match.Index, literalCallSpans))
                continue;

            literalCallSpans.Add(new Vector2Int(match.Index, match.Length));
            string id = ResolveIoTReference(match.Groups[3].Value, aliasesToId);

            if (!ValidateIoTCommand(id, "RGB:0:0:0", supportedById, out error))
                return false;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(\s*(['""])(.*?)\2\s*,", RegexOptions.IgnoreCase))
        {
            literalCallSpans.Add(new Vector2Int(match.Index, match.Length));
            string id = ResolveIoTReference(match.Groups[3].Value, aliasesToId);

            if (!ValidateIoTCommand(id, "RGB:0:0:0", supportedById, out error))
                return false;
        }

        foreach (Match match in Regex.Matches(lua, @"iot\s*:\s*(On|Off|Send|LightBulb|Lightbuld|SetRGB|setRGB|SetRed|setRed|SetGreen|setGreen|SetBlue|setBlue)\s*\(", RegexOptions.IgnoreCase))
        {
            bool matchedLiteral = false;
            foreach (var span in literalCallSpans)
            {
                if (match.Index >= span.x && match.Index < span.x + span.y)
                {
                    matchedLiteral = true;
                    break;
                }
            }

            if (!matchedLiteral)
            {
                error = "IoT calls must use literal listed device IDs and supported literal commands. For bulb RGB use iot:LightBulb(\"BULB01\"):SetRGB(r,g,b) or SetRed/SetGreen/SetBlue with a literal listed ID.";
                return false;
            }
        }

        return true;
    }

    private bool SpanAlreadyRecorded(int index, List<Vector2Int> spans)
    {
        foreach (var span in spans)
        {
            if (index >= span.x && index < span.x + span.y)
                return true;
        }

        return false;
    }

    private bool ValidateDynamicIoTCommandExpression(
        string id,
        string commandExpression,
        Dictionary<string, HashSet<string>> supportedById,
        out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(id) || !supportedById.TryGetValue(id, out var commands))
        {
            error = "Invalid IoT device ID: " + id;
            return false;
        }

        string normalizedExpression = IOTManager.NormalizeCommand(commandExpression);
        bool wantsVolume = normalizedExpression.Contains("VOLUME");
        bool wantsChannel = normalizedExpression.Contains("CHANNEL");
        bool wantsRgb = normalizedExpression.Contains("RGB");
        bool wantsHsv = normalizedExpression.Contains("HSV");
        bool wantsBrightness = normalizedExpression.Contains("BRIGHTNESS");
        bool wantsTemperature = normalizedExpression.Contains("TEMPERATURE");

        if (wantsVolume && IoTCommandPrefixIsSupported("VOLUME", commands))
            return true;

        if (wantsChannel && IoTCommandPrefixIsSupported("CHANNEL", commands))
            return true;

        if (wantsRgb && IoTCommandPrefixIsSupported("RGB", commands))
            return true;

        if (wantsHsv && IoTCommandPrefixIsSupported("HSV", commands))
            return true;

        if (wantsBrightness && IoTCommandPrefixIsSupported("BRIGHTNESS", commands))
            return true;

        if (wantsTemperature && IoTCommandPrefixIsSupported("TEMPERATURE", commands))
            return true;

        error = "Dynamic IoT Send commands are only allowed for listed VOLUME, CHANNEL, RGB, HSV, BRIGHTNESS, or TEMPERATURE commands.";
        return false;
    }

    private bool IoTCommandPrefixIsSupported(string prefix, HashSet<string> supportedCommands)
    {
        foreach (var supported in supportedCommands)
        {
            if (IOTManager.NormalizeCommand(supported).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void AddIoTAlias(Dictionary<string, string> aliasesToId, string alias, string id)
    {
        string normalizedAlias = IOTManager.NormalizeDeviceId(alias);
        if (string.IsNullOrEmpty(normalizedAlias) || string.IsNullOrWhiteSpace(id))
            return;

        if (!aliasesToId.ContainsKey(normalizedAlias))
            aliasesToId[normalizedAlias] = id;
    }

    private string ResolveIoTReference(string reference, Dictionary<string, string> aliasesToId)
    {
        string normalized = IOTManager.NormalizeDeviceId(reference);
        if (string.IsNullOrEmpty(normalized))
            return "";

        if (aliasesToId.TryGetValue(normalized, out var exactId))
            return exactId;

        string matchedId = "";
        foreach (var kv in aliasesToId)
        {
            if (kv.Key.Contains(normalized) || normalized.Contains(kv.Key))
            {
                if (!string.IsNullOrEmpty(matchedId) && !string.Equals(matchedId, kv.Value, StringComparison.OrdinalIgnoreCase))
                    return normalized;

                matchedId = kv.Value;
            }
        }

        return string.IsNullOrEmpty(matchedId) ? normalized : matchedId;
    }

    private bool ValidateIoTCommand(
        string id,
        string command,
        Dictionary<string, HashSet<string>> supportedById,
        out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(id))
        {
            error = "Invalid IoT device ID.";
            return false;
        }

        if (!supportedById.TryGetValue(id, out var commands))
        {
            error = "Invalid IoT device ID: " + id;
            return false;
        }

        if (string.IsNullOrWhiteSpace(command) || !IoTCommandIsSupported(command, commands))
        {
            error = $"Unsupported IoT command '{command}' for device {id}.";
            return false;
        }

        return true;
    }

    private bool IoTCommandIsSupported(string command, HashSet<string> supportedCommands)
    {
        command = IOTManager.NormalizeCommand(command);
        if (supportedCommands.Contains(command))
            return true;

        foreach (var supported in supportedCommands)
        {
            string normalizedSupported = IOTManager.NormalizeCommand(supported);
            if (normalizedSupported.StartsWith("VOLUME") && command.StartsWith("VOLUME"))
                return IoTCommandHasNumericValue(command);

            if (normalizedSupported.StartsWith("CHANNEL") && command.StartsWith("CHANNEL"))
                return IoTCommandHasNumericValue(command);

            if (normalizedSupported.StartsWith("RGB") && command.StartsWith("RGB"))
                return IoTCommandHasNumericTuple(command, 3);

            if (normalizedSupported.StartsWith("HSV") && command.StartsWith("HSV"))
                return IoTCommandHasNumericTuple(command, 3);

            if (normalizedSupported.StartsWith("BRIGHTNESS") && command.StartsWith("BRIGHTNESS"))
                return IoTCommandHasNumericValue(command);

            if (normalizedSupported.StartsWith("TEMPERATURE") && command.StartsWith("TEMPERATURE"))
                return IoTCommandHasNumericValue(command);

            if (normalizedSupported.StartsWith("IP") && command.StartsWith("IP"))
                return IoTCommandHasTextValue(command);
        }

        return false;
    }

    private bool IoTCommandHasNumericValue(string command)
    {
        int index = command.IndexOfAny(new[] { ':', '_', '=', ' ' });
        if (index < 0 || index + 1 >= command.Length)
            return false;

        return float.TryParse(command.Substring(index + 1), out _);
    }

    private bool IoTCommandHasNumericTuple(string command, int expectedCount)
    {
        int index = command.IndexOfAny(new[] { ':', '_', '=', ' ', '/' });
        if (index < 0 || index + 1 >= command.Length)
            return false;

        string raw = command.Substring(index + 1);
        string[] parts = raw.Split(new[] { ':', '_', '=', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expectedCount)
            return false;

        foreach (var part in parts)
        {
            if (!float.TryParse(part, out _))
                return false;
        }

        return true;
    }

    private bool IoTCommandHasTextValue(string command)
    {
        int index = command.IndexOfAny(new[] { ':', '_', '=', ' ', '/' });
        return index >= 0 && index + 1 < command.Length && !string.IsNullOrWhiteSpace(command.Substring(index + 1));
    }

    private void BeginPromptLogIfPossible()
    {
        activePromptLogObject = target != null ? target.GetComponentInParent<ProgramableObject>() : null;
        activePromptLogId = null;
        activePromptLogStartTime = Time.realtimeSinceStartup;

        if (activePromptLogObject != null)
            activePromptLogId = activePromptLogObject.BeginPromptLog(naturalLanguageIntent, mode.ToString(), model);
    }

    private void CompletePromptLogSuccess(string lua)
    {
        if (activePromptLogObject == null || string.IsNullOrEmpty(activePromptLogId))
            return;

        activePromptLogObject.CompletePromptLogSuccess(
            activePromptLogId,
            lua,
            Time.realtimeSinceStartup - activePromptLogStartTime
        );
    }

    private void Fail(string message)
    {
        lastError = string.IsNullOrWhiteSpace(message) ? "Unknown Lua generation error." : message;
        Debug.LogError("[OpenAILuaGenerator] " + lastError);
        PublishReturnMessage(lastError);

        if (activePromptLogObject != null && !string.IsNullOrEmpty(activePromptLogId))
        {
            activePromptLogObject.CompletePromptLogFailure(
                activePromptLogId,
                lastError,
                Time.realtimeSinceStartup - activePromptLogStartTime
            );
        }
    }

    private void PublishReturnMessage(string message)
    {
        if (displayMode != ReturnDisplayMode.Off && returnMessageText != null)
            returnMessageText.text = message;

        OnReturnMessage?.Invoke(message);
    }

    private UnityWebRequest CreateChatCompletionRequest(ChatRequest req)
    {
        var www = new UnityWebRequest(
            "https://api.openai.com/v1/chat/completions", "POST"
        );

        www.uploadHandler = new UploadHandlerRaw(
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(req))
        );
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", "Bearer " + apiKey);
        return www;
    }

    private string BuildRepairPrompt(string originalUserPrompt, string rejectedLua, string validationError)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The previous Lua response failed validation.");
        sb.AppendLine("Return a corrected FULL Lua script only. No markdown, no explanation.");
        sb.AppendLine();
        sb.AppendLine("VALIDATION ERROR:");
        sb.AppendLine(validationError);
        sb.AppendLine();
        sb.AppendLine("IMPORTANT REPAIR RULES:");
        sb.AppendLine("- Preserve the user's original intent.");
        sb.AppendLine("- Include start/update/on_trigger/on_collision lifecycle functions.");
        sb.AppendLine("- If the error says the user touched the object, use hand/user touch APIs in update(), not on_collision or col:Matches.");
        sb.AppendLine("- For hand touch use user:IsAnyHandCloseToThisObject(distance) or self.programable:IsTouching().");
        sb.AppendLine("- If the error says breathing/size-following, implement scale animation on self.shape in update(), with IoT threshold checks; do not use on_collision or col:Matches.");
        sb.AppendLine("- If the error says clap, use user:IsHandsClose(threshold) or user:GetHandDistance() in update() with self.wasClapping edge detection.");
        sb.AppendLine("- If the error says close/near a named scene object, use scene lookup plus user:GetHeadPosition() and manual distance math; do not use this-object proximity APIs.");
        sb.AppendLine("- If the error says object size/distance, use scene lookup plus TransformProxy.position/localScale; for the selected object's visible size use self.shape.localScale, not collision or user body distance APIs.");
        sb.AppendLine("- Keep IoT device IDs and commands from the original context.");
        sb.AppendLine();
        sb.AppendLine("ORIGINAL FULL PROMPT CONTEXT:");
        sb.AppendLine(originalUserPrompt);
        sb.AppendLine();
        sb.AppendLine("REJECTED LUA:");
        sb.AppendLine(rejectedLua);
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
