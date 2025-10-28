using System;
using System.Collections;
using System.Collections.Generic;
using System.Text; // for Encoding.UTF8
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class PromptedMatterDialogue : MonoBehaviour
{
    [Header("Wiring")]
    public LLMProfile llmProfile;           // reuse your existing LLMProfile (loads API key from Resources/Secrets if configured)
    public DialogueProfile dialogueProfile; // tone & JSON contract
    public OpenAIProfileGenerator generator; // called when agreement is reached
    public PromptedMatter targetMatter;     // the object we’re negotiating for

    [Header("State")]
    [TextArea(4,10)] public string lastAgreementText;
    [SerializeField] private int turnCount = 0;

    // --- UI events ---
    public event Action<string> OnAssistantUtter; // human-readable assistant line for chat UI
    public event Action<string> OnStatus;         // status lines (e.g., contacting model…)
    public event Action<string> OnError;          // surfaced errors

    // --- Runtime convo buffer (system + alternating user/assistant) ---
    [Serializable] private class ChatMessage { public string role; public string content; public ChatMessage(string r,string c){role=r;content=c;} }
    private readonly List<ChatMessage> _thread = new();

    // --- OpenAI payload/response types (like in your generator) ---
    [Serializable] private class ChatPayload
    {
        public string model; public float temperature; public int max_tokens; public ChatMessage[] messages;
    }
    [Serializable] private class OpenAIChatResponse
    {
        [Serializable] public class Msg { public string role; public string content; }
        [Serializable] public class Choice { public Msg message; }
        public Choice[] choices;
    }

    // --- Negotiation JSON contract type ---
    [Serializable] private class NegotiationReply
    {
        public string act; // clarify | propose | agree | decline
        public string assistant_utterance;
        public string agreement_text;
        public string notes;
    }

    // Public entry: start a negotiation with user's initial idea
    public void BeginNegotiation(string userIdea)
    {
        if (llmProfile == null || dialogueProfile == null || generator == null || targetMatter == null)
        {
            var msg = "[PromptedMatterDialogue] Missing references (llmProfile/dialogueProfile/generator/targetMatter).";
            Debug.LogError(msg);
            TryStatusOrError(false, msg);
            return;
        }

        turnCount = 0;
        _thread.Clear();

        // Build system prompt that forces the tiny JSON contract
        string system = BuildSystemMessage();
        _thread.Add(new ChatMessage("system", system));

        // Add object context and (optionally) previous state to the first user turn
        string context = targetMatter.GetLLMContext();
        string prev = generator != null && generator.continueEditing ? ("\n\n" + targetMatter.GetPreviousStateContext()) : "";
        string firstUser = $"User idea: {userIdea}\n\nObject context:\n{context}{prev}";
        _thread.Add(new ChatMessage("user", firstUser));

        TryStatus("Negotiation started.");
        StartCoroutine(CoStep());
    }

    // Public: push another user reply inside ongoing negotiation (optional UI button)
    public void ContinueWithUserReply(string userReply)
    {
        if (_thread.Count == 0) { Debug.LogWarning("[PromptedMatterDialogue] Call BeginNegotiation first."); TryStatus("Please start negotiation first."); return; }
        _thread.Add(new ChatMessage("user", userReply));
        StartCoroutine(CoStep());
    }

    // System prompt builder
    private string BuildSystemMessage()
    {
        string persona = dialogueProfile.persona ?? "";
        string style = dialogueProfile.style ?? "";
        string contract = dialogueProfile.negotiationJsonContract ?? "";

        // Keep it strict JSON and small act-set
        return
$@"{persona}

{style}

Rules:
- You are negotiating ONLY. Do not output code or particles.
- Respond with STRICT JSON (no markdown, no prose outside JSON).
- Use the contract exactly as specified.
- Max {dialogueProfile.maxTurns} total assistant turns. Prefer to reach 'agree' quickly.
- Agreement text must be <= {dialogueProfile.maxAgreementChars} chars, concrete, and safe.
- Examples of good agreement text: 
  - ""Gently emit white steam from the cup rim; no Lua changes.""
  - ""Glow softly when touched; stop glowing when not touched.""

Contract:
{contract}";
    }

    // One assistant step
    private IEnumerator CoStep()
    {
        turnCount++;
        if (turnCount > dialogueProfile.maxTurns + 1) // +1 safety
        {
            TryStatus("Reached max turns without agreement.");
            yield break;
        }

        // Build payload
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
        if (string.IsNullOrEmpty(key))
        {
            TryStatusOrError(false, "API key is empty (check Resources/Secrets/openai_api_key.txt or LLMProfile).");
            yield break;
        }
        req.SetRequestHeader("Authorization", "Bearer " + key);

        TryStatus("Contacting model…");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            TryStatusOrError(false, "LLM error: " + req.error + "\n" + req.downloadHandler.text);
            yield break;
        }

        OpenAIChatResponse root = null;
        try { root = JsonUtility.FromJson<OpenAIChatResponse>(req.downloadHandler.text); }
        catch (Exception ex)
        {
            TryStatusOrError(false, "Parse response error: " + ex.Message + "\n" + req.downloadHandler.text);
            yield break;
        }

        if (root == null || root.choices == null || root.choices.Length == 0 || root.choices[0].message == null)
        {
            TryStatusOrError(false, "Unexpected chat response:\n" + req.downloadHandler.text);
            yield break;
        }

        string assistantContent = root.choices[0].message.content?.Trim() ?? "";
        var reply = JsonUtility.FromJson<NegotiationReply>(assistantContent);
        if (reply == null)
        {
            TryStatusOrError(false, "Could not parse negotiation JSON:\n" + assistantContent);
            yield break;
        }

        // Save assistant JSON to the thread (keeps full JSON for continuity)
        _thread.Add(new ChatMessage("assistant", assistantContent));

        // Emit the human-facing utterance
        OnAssistantUtter?.Invoke(reply.assistant_utterance);

        // Handle the act
        var act = (reply.act ?? "").ToLowerInvariant();
        if (act == "agree")
        {
            lastAgreementText = Truncate(reply.agreement_text, dialogueProfile.maxAgreementChars).Trim();
            if (string.IsNullOrEmpty(lastAgreementText))
            {
                TryStatus("Agree received, but empty agreement_text. Stopping.");
                yield break;
            }

            TryStatus("Agreed. Applying: " + lastAgreementText);
            // Hand to generator (applies Lua/particles and remembers state)
            generator.targetMatter = targetMatter;
            generator.GenerateFromUserPrompt(lastAgreementText);
            yield break;
        }
        else if (act == "decline")
        {
            TryStatus("Model declined to proceed. No changes applied.");
            yield break;
        }
        else
        {
            // clarify | propose → wait for user's next message
            TryStatus("Waiting for your reply…");
            yield break;
        }
    }

    private string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));

    private void TryStatus(string msg) { try { OnStatus?.Invoke(msg); } catch {} }
    private void TryStatusOrError(bool ok, string msg)
    {
        if (ok) TryStatus(msg); else { try { OnError?.Invoke(msg); } catch {} }
    }
}
