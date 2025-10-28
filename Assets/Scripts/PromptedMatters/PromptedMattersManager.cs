using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class PromptedMattersManager : MonoBehaviour
{
    [Header("Shared Assets")]
    public LLMProfile llmProfile;
    public DialogueProfile dialogueProfile;

    [Header("Singleton Generator (owned here)")]
    public OpenAIProfileGenerator generator; // exactly one in the scene

    [Header("Runtime")]
    public PromptedMatter currentTarget;          // object currently being edited
    private MatterDialogueAgent currentAgent;     // active per-object agent
    private readonly List<ChatMessage> _thread = new();
    private bool _busy;

    [Serializable] private class ChatMessage { public string role; public string content; public ChatMessage(string r,string c){role=r;content=c;} }
    [Serializable] private class ChatPayload { public string model; public float temperature; public int max_tokens; public ChatMessage[] messages; }
    [Serializable] private class OpenAIChatResponse { [Serializable] public class Msg { public string role; public string content; } [Serializable] public class Choice { public Msg message; } public Choice[] choices; }
    [Serializable] private class NegotiationReply { public string act; public string assistant_utterance; public string agreement_text; public string notes; }

    public bool IsBusy => _busy;

    // ---- Called by per-object agents ----
    public void BeginFor(MatterDialogueAgent agent, string seedIdea)
    {
        if (_busy) { agent?.RaiseError("Manager is busy with another object. Try again shortly."); return; }
        if (!Sanity(out var err)) { agent?.RaiseError(err); return; }

        currentAgent = agent;
        currentTarget = agent ? agent.matter : null;
        _thread.Clear();

        string system = BuildSystemMessage();
        _thread.Add(new ChatMessage("system", system));

        string context = currentTarget ? currentTarget.GetLLMContext() : "object_profile:{}";
        string prev = (agent.forwardPreviousState && currentTarget != null) ? ("\n\n" + currentTarget.GetPreviousStateContext()) : "";
        string firstUser = $"User idea: {seedIdea}\n\nObject context:\n{context}{prev}";
        _thread.Add(new ChatMessage("user", firstUser));

        agent?.RaiseStatus("Negotiation started.");
        StartCoroutine(CoStep());
    }

    public void ContinueFor(MatterDialogueAgent agent, string userReply)
    {
        if (_busy && agent != currentAgent) { agent?.RaiseError("Another session is active."); return; }
        if (agent != currentAgent) { agent?.RaiseError("No active session for this object. Start first."); return; }

        _thread.Add(new ChatMessage("user", userReply));
        StartCoroutine(CoStep());
    }

    // ---- Dialogue brain ----
    private string BuildSystemMessage()
    {
        var p = dialogueProfile;
        string persona = p ? (p.persona ?? "") : "";
        string style = p ? (p.style ?? "") : "";
        string flavor = p ? (p.utteranceFlavor ?? "") : "";
        int maxTurns = p ? p.maxTurns : 3;
        int maxAgreementChars = p ? p.maxAgreementChars : 240;
        int maxUtterance = p ? Mathf.Max(60, p.maxUtteranceChars) : 160; // safety floor

        return
$@"{persona}

{style}

UTTERANCE STYLE (for assistant_utterance only):
{flavor}
- Hard cap: assistant_utterance must be <= {maxUtterance} characters.

Rules:
- You are negotiating ONLY. Do not output code or particles.
- Respond with STRICT JSON (no markdown, no prose outside JSON).
- Use the contract exactly as specified.
- Max {maxTurns} total assistant turns. Prefer to reach 'agree' quickly.
- Agreement text must be <= {maxAgreementChars} chars, concrete, and safe.

Contract:
{(p ? p.negotiationJsonContract : "")}";
    }

    private IEnumerator CoStep()
    {
        _busy = true;

        int maxTurns = dialogueProfile ? dialogueProfile.maxTurns : 3;
        int assistantTurns = 0; foreach (var m in _thread) if (m.role == "assistant") assistantTurns++;
        if (assistantTurns >= maxTurns)
        {
            currentAgent?.RaiseStatus("Reached max turns without agreement.");
            currentAgent?.RaiseFinished(false, "");
            _busy = false; yield break;
        }

        var payload = new ChatPayload
        {
            model = llmProfile.model,
            temperature = Mathf.Clamp(llmProfile.temperature, 0.1f, 0.8f),
            max_tokens = Mathf.Min(800, llmProfile.maxTokens > 0 ? llmProfile.maxTokens : 800),
            messages = _thread.ToArray()
        };

        string json = JsonUtility.ToJson(payload);
        using var req = new UnityWebRequest(llmProfile.baseUrl, "POST");
        byte[] body = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        var key = llmProfile.ResolveApiKey();
        if (string.IsNullOrEmpty(key)) { currentAgent?.RaiseError("Empty API key (Resources/Secrets/openai_api_key.txt or profile)."); _busy = false; yield break; }
        req.SetRequestHeader("Authorization", "Bearer " + key);

        currentAgent?.RaiseStatus("Contacting model…");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            currentAgent?.RaiseError("LLM error: " + req.error + "\n" + req.downloadHandler.text);
            _busy = false; yield break;
        }

        OpenAIChatResponse root = null;
        try { root = JsonUtility.FromJson<OpenAIChatResponse>(req.downloadHandler.text); }
        catch (Exception ex) { currentAgent?.RaiseError("Parse response error: " + ex.Message); _busy = false; yield break; }

        if (root?.choices == null || root.choices.Length == 0 || root.choices[0].message == null)
        { currentAgent?.RaiseError("Unexpected chat response."); _busy = false; yield break; }

        string assistantContent = root.choices[0].message.content?.Trim() ?? "";
        var reply = JsonUtility.FromJson<NegotiationReply>(assistantContent);
        if (reply == null) { currentAgent?.RaiseError("Could not parse negotiation JSON."); _busy = false; yield break; }

        _thread.Add(new ChatMessage("assistant", assistantContent));
        if (!string.IsNullOrEmpty(reply.assistant_utterance))
            currentAgent?.RaiseAssistantUtter(reply.assistant_utterance);

        string act = (reply.act ?? "").ToLowerInvariant();
        int maxAgreementChars = dialogueProfile ? dialogueProfile.maxAgreementChars : 240;

        if (act == "agree")
        {
            string agreement = Truncate(reply.agreement_text, maxAgreementChars).Trim();
            if (string.IsNullOrEmpty(agreement))
            {
                currentAgent?.RaiseStatus("Agree with empty agreement_text; stop.");
                currentAgent?.RaiseFinished(false, "");
                _busy = false; yield break;
            }

            currentAgent?.RaiseStatus("Applying: " + agreement);

            generator.targetMatter = currentTarget;
            generator.continueEditing = currentAgent.forwardPreviousState;
            generator.applyLua = currentAgent.applyLua;
            generator.applyParticles = currentAgent.applyParticles;
            generator.GenerateFromUserPrompt(agreement);

            currentAgent?.RaiseFinished(true, agreement);
            _busy = false; yield break;
        }
        else if (act == "decline")
        {
            currentAgent?.RaiseStatus("Declined. No changes.");
            currentAgent?.RaiseFinished(false, "");
            _busy = false; yield break;
        }
        else
        {
            currentAgent?.RaiseStatus(string.IsNullOrEmpty(reply.assistant_utterance) ? "Waiting for your reply…" : reply.assistant_utterance);
            _busy = false; yield break;
        }
    }

    private bool Sanity(out string err)
    {
        if (!llmProfile) { err = "Manager missing LLMProfile."; return false; }
        if (!dialogueProfile) { err = "Manager missing DialogueProfile."; return false; }
        if (!generator) { err = "Manager missing OpenAIProfileGenerator."; return false; }
        err = null; return true;
    }

    private string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…");
}
