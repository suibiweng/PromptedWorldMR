using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class LayoutLLMService : MonoBehaviour
{
    [Header("OpenAI")]
    [Tooltip("Loaded from Resources/Secrets/openai_api_key if empty")]
    public string apiKey;
    public string model = "gpt-4.1-mini";
    public float temperature = 0.2f;

    void Awake()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            TextAsset keyAsset = Resources.Load<TextAsset>("Secrets/openai_api_key");
            if (keyAsset == null)
            {
                Debug.LogError("[LayoutLLMService] Missing API key at Resources/Secrets/openai_api_key");
            }
            else
            {
                apiKey = keyAsset.text.Trim();
            }
        }
    }

    // ================================
    // PUBLIC API
    // ================================
    public IEnumerator RequestLayoutJson(
        string userPrompt,
        List<string> allowedIds,
        Action<string> onJson,
        Action<string> onError
    )
    {
        string systemPrompt = GetSystemPrompt(allowedIds);

        var messages = new[]
        {
            new Message { role = "system", content = systemPrompt },
            new Message { role = "user", content = userPrompt }
        };

        yield return Send(messages, onJson, onError);
    }

    // ================================
    // PROMPT (CRITICAL)
    // ================================
    private string GetSystemPrompt(List<string> allowedIds)
    {
        return
$@"You are a spatial layout assistant for Mixed Reality.

=== CRITICAL CONSTRAINT ===
You MUST use ONLY the instance ids listed below.
- Do NOT invent new ids
- Do NOT rename
- Case-sensitive
- Output EXACTLY {allowedIds.Count} instances

Allowed instance ids:
{string.Join("\n", allowedIds.Select(id => "- " + id))}

=== MIXED REALITY CONTEXT ===
- Unity units are meters (1 unit ≈ 1 meter)
- Objects already exist in the scene
- You are ONLY repositioning and rescaling them

=== SCALE RULES ===
- scale is ABSOLUTE local scale
- Default object scale in this project is usually 0.2, 0.2, 0.2
- Keep scale close to default unless resizing is clearly implied
- Never reuse one object's scale for another

=== OUTPUT RULES ===
- Output ONLY valid JSON
- No markdown
- No explanations

=== JSON FORMAT ===
{{
  ""layout_name"": string,
  ""space"": ""local"",
  ""instances"": [
    {{
      ""id"": string,
      ""position"": {{ ""x"": float, ""y"": float, ""z"": float }},
      ""rotationEuler"": {{ ""x"": float, ""y"": float, ""z"": float }},
      ""scale"": {{ ""x"": float, ""y"": float, ""z"": float }}
    }}
  ]
}}";
    }

    // ================================
    // NETWORK
    // ================================
    private IEnumerator Send(
        Message[] messages,
        Action<string> onJson,
        Action<string> onError
    )
    {
        var req = new ChatRequest
        {
            model = model,
            temperature = temperature,
            messages = messages
        };

        string json = JsonUtility.ToJson(req);

        using var www = new UnityWebRequest(
            "https://api.openai.com/v1/chat/completions",
            "POST"
        );

        www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(www.error);
            yield break;
        }

        var resp = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);
        if (resp == null || resp.choices == null || resp.choices.Count == 0)
        {
            onError?.Invoke("Invalid response");
            yield break;
        }

        // ================================
        // DEBUG: PRINT RAW RETURN JSON
        // ================================
        string returnedJson = resp.choices[0].message.content;
        Debug.Log(
            "[LayoutLLMService] 🧾 RAW RETURN JSON FROM OPENAI:\n" +
            returnedJson
        );

        onJson?.Invoke(returnedJson);
    }

    // ================================
    // DTOs
    // ================================
    [Serializable] private class Message { public string role; public string content; }
    [Serializable] private class ChatRequest { public string model; public float temperature; public Message[] messages; }
    [Serializable] private class Choice { public Message message; }
    [Serializable] private class ChatResponse { public List<Choice> choices; }
}
