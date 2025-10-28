using System;
using System.Collections;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class OpenAIParagraphProcessor : MonoBehaviour
{
    [Header("OpenAI")]
    public string model = "gpt-4o-mini";
    public string apiBase = "https://api.openai.com/v1";
    [Range(0f, 2f)] public float temperature = 0.3f;
    public int maxTokens = 512;
    public bool verboseLogs = false;

    [Header("Context Limits")]
    [Tooltip("Hard cap for request JSON body (approx). Keep under typical limits for safety.")]
    public int maxRequestChars = 140_000;  // safety cap
    [Tooltip("If a single paragraph is longer than this, we truncate with an ellipsis.")]
    public int maxParagraphChars = 4000;
    [Tooltip("Show this many characters of each non-selected paragraph in the corpus.")]
    public int nonSelectedPreviewChars = 600;

    string _apiKey;

    void Awake()
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            try
            {
                var ta = Resources.Load<TextAsset>("Secrets/openai_api_key");
                if (ta != null) _apiKey = ta.text.Trim();
            }
            catch { }

            if (string.IsNullOrEmpty(_apiKey))
                _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrEmpty(_apiKey))
                Debug.LogWarning("[OpenAI] No API key found. Call SetApiKey(...) or add Resources/Secrets/openai_api_key.txt");
        }
    }

    public void SetApiKey(string key) => _apiKey = key?.Trim();

    // ---------------- BASIC (old) ----------------
    public void ProcessParagraph(string paragraph, string userPrompt, Action<string> onDone, Action<string> onError = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            onError?.Invoke("Missing OpenAI API key.");
            return;
        }
        StartCoroutine(CoChat(BuildBasicMessages(paragraph, userPrompt), onDone, onError));
    }

    // -------------- NEW: CORPUS-AWARE --------------
    public void ProcessParagraphWithCorpus(List<string> paragraphs, int selectedIndex, string userPrompt, Action<string> onDone, Action<string> onError = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            onError?.Invoke("Missing OpenAI API key.");
            return;
        }
        if (paragraphs == null || paragraphs.Count == 0)
        {
            onError?.Invoke("No paragraphs provided.");
            return;
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0, paragraphs.Count - 1);

        var msgs = BuildCorpusMessages(paragraphs, selectedIndex, userPrompt, maxRequestChars, maxParagraphChars, nonSelectedPreviewChars);
        StartCoroutine(CoChat(msgs, onDone, onError));
    }

    // ---------------- Message builders ----------------
    List<ChatMessage> BuildBasicMessages(string paragraph, string userPrompt)
    {
        string system = "You are a helpful assistant. Given a paragraph and a user instruction, " +
                        "return ONLY the processed result text. No preambles, no extra commentary.";

        string user = $"USER PROMPT:\n{userPrompt}\n\nPARAGRAPH:\n{paragraph}";

        return new List<ChatMessage>
        {
            new ChatMessage{ role="system", content=system },
            new ChatMessage{ role="user",   content=user }
        };
    }

    List<ChatMessage> BuildCorpusMessages(
        List<string> paragraphs,
        int selectedIndex,
        string userPrompt,
        int reqCap,
        int perParaCap,
        int nonSelPreviewCap)
    {
        // Normalize & clamp paragraphs
        string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\0", "");
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            // compact multiple spaces/newlines
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[ \t]+", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(\n\s*){3,}", "\n\n");
            if (s.Length > perParaCap) s = s.Substring(0, perParaCap) + " …";
            return s.Trim();
        }

        var cleaned = paragraphs.Select(Clean).ToList();

        // Build a numbered corpus summary (short previews for non-selected)
        var sbCorpus = new StringBuilder(4096);
        sbCorpus.AppendLine("CORPUS (numbered paragraphs, in reading order)");
        for (int i = 0; i < cleaned.Count; i++)
        {
            var text = cleaned[i];
            bool isSel = (i == selectedIndex);
            if (string.IsNullOrWhiteSpace(text)) text = "(no text)";

            if (isSel)
            {
                sbCorpus.AppendLine($"[P{i}] (SELECTED)");
                sbCorpus.AppendLine(text);
            }
            else
            {
                string prev = text.Length > nonSelPreviewCap ? text.Substring(0, nonSelPreviewCap) + " …" : text;
                sbCorpus.AppendLine($"[P{i}] (preview)");
                sbCorpus.AppendLine(prev);
            }
            sbCorpus.AppendLine();
        }

        string instruction =
@"You are given a user instruction and a corpus of numbered paragraphs from the same page/document.
Rules:
1) Answer primarily using the SELECTED paragraph [P{SEL}].
2) If the answer requires info not in [P{SEL}], you MAY draw from other paragraphs. If you do, 
   clearly note which paragraphs you used like: (sources: P0, P3).
3) If the answer is not supported by ANY paragraph, say so and briefly explain what's missing.
4) Keep your answer concise and directly address the user's prompt.
5) Do NOT invent citations; only refer to paragraph IDs that actually support the claim.
6) Output plain text only.";

        instruction = instruction.Replace("{SEL}", selectedIndex.ToString());

        // Build user message
        var sbUser = new StringBuilder(4096);
        sbUser.AppendLine(instruction);
        sbUser.AppendLine();
        sbUser.AppendLine("USER PROMPT:");
        sbUser.AppendLine(userPrompt);
        sbUser.AppendLine();
        sbUser.AppendLine(sbCorpus.ToString());

        // Ensure under request char cap
        string userContent = sbUser.ToString();
        if (userContent.Length > reqCap)
        {
            // Trim previews further if needed
            int target = Mathf.Max(10_000, reqCap - 2000);
            userContent = userContent.Substring(0, target) + "\n… (truncated corpus previews for length)\n";
        }

        string system = "You are a careful, citation-minded assistant. Follow the rules exactly.";

        return new List<ChatMessage>
        {
            new ChatMessage{ role="system", content = system },
            new ChatMessage{ role="user",   content = userContent }
        };
    }

    // ---------------- HTTP plumbing ----------------
    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class ChatRequest
    {
        public string model;
        public float temperature;
        public int max_tokens;
        public List<ChatMessage> messages;
    }
    [Serializable] class ChatChoiceMessage { public string role; public string content; }
    [Serializable] class ChatChoice { public int index; public ChatChoiceMessage message; public object logprobs; public string finish_reason; }
    [Serializable] class ChatResponse { public List<ChatChoice> choices; }

    IEnumerator CoChat(List<ChatMessage> messages, Action<string> onDone, Action<string> onError)
    {
        string url = $"{apiBase.TrimEnd('/')}/chat/completions";
        var reqObj = new ChatRequest
        {
            model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model,
            temperature = temperature,
            max_tokens = Mathf.Clamp(maxTokens, 32, 4096),
            messages = messages
        };

        string json = JsonUtility.ToJson(reqObj);
        if (verboseLogs) Debug.Log("[OpenAI →] " + Truncate(json, 4000));

        using var www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        www.uploadHandler   = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", "Bearer " + _apiKey);

        yield return www.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
        bool ok = (www.result == UnityWebRequest.Result.Success);
#else
        bool ok = !(www.isNetworkError || www.isHttpError);
#endif

        if (!ok)
        {
            string err = $"HTTP {(int)www.responseCode}: {www.error}\n{www.downloadHandler?.text}";
            if (verboseLogs) Debug.LogError("[OpenAI] " + err);
            onError?.Invoke(err);
            yield break;
        }

        var txt = www.downloadHandler.text;
        if (verboseLogs) Debug.Log("[OpenAI ←] " + Truncate(txt, 4000));

        ChatResponse parsed = null;
        try { parsed = JsonUtility.FromJson<ChatResponse>(txt); } catch { }

        string content = null;
        if (parsed != null && parsed.choices != null && parsed.choices.Count > 0)
            content = parsed.choices[0]?.message?.content;

        if (string.IsNullOrWhiteSpace(content))
        {
            // naive fallback extraction
            int i = txt.IndexOf("\"content\":");
            if (i >= 0)
            {
                int start = txt.IndexOf('"', i + 10) + 1;
                int end = txt.IndexOf('"', start);
                if (start > 0 && end > start) content = txt.Substring(start, end - start);
                content = content?.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            onError?.Invoke("Empty response from model.");
            yield break;
        }

        onDone?.Invoke(content.Trim());
    }

    static string Truncate(string s, int max) => (s != null && s.Length > max) ? s.Substring(0, max) + " …" : s;
}
