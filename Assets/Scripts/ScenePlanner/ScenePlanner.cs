using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class ScenePlanner : MonoBehaviour
{
    [Header("OpenAI")]
    [SerializeField] private string apiKey;
    [SerializeField] private string apiKeyResourcePath = "Secrets/openai_api_key";
    [SerializeField] private string model = "gpt-4.1-mini";
    [Range(0f, 1f)] public float temperature = 0.1f;

    [Header("Input")]
    [TextArea(2, 6)]
    public string userDescription;

    [Header("Output")]
    [TextArea(10, 30)]
    public string rawScenePlanJson;
    public ScenePlan parsedPlan;

    [Header("Build Target")]
    public WorldBuilder worldBuilder;

    public void GenerateScenePlan()
    {
        StartCoroutine(Co_Generate());
    }

    private IEnumerator Co_Generate()
    {
        if (string.IsNullOrWhiteSpace(userDescription))
            yield break;

        string key = LoadApiKey();
        if (string.IsNullOrEmpty(key))
            yield break;

        var messages = new List<Message>
        {
            new Message { role = "system", content = ScenePlannerPrompt.SYSTEM_PROMPT },
            new Message { role = "user", content = userDescription }
        };

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
        www.SetRequestHeader("Authorization", "Bearer " + key);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
            yield break;

        var resp = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);
        if (resp == null || resp.choices == null || resp.choices.Count == 0)
            yield break;

        rawScenePlanJson = ScenePlanJsonCleaner.Clean(
            resp.choices[0].message.content
        );

        parsedPlan = JsonUtility.FromJson<ScenePlan>(rawScenePlanJson);

        if (ScenePlanValidator.Validate(parsedPlan))
        {
            Debug.Log("[ScenePlanner] ScenePlan OK:\n" + rawScenePlanJson);

            if (worldBuilder != null)
                worldBuilder.Build(parsedPlan);
        }
    }

    private string LoadApiKey()
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey.Trim();

        var ta = Resources.Load<TextAsset>(apiKeyResourcePath);
        return ta ? ta.text.Trim() : null;
    }

    // DTOs
    [Serializable] private class Message { public string role; public string content; }
    [Serializable] private class ChatRequest { public string model; public float temperature; public List<Message> messages; }
    [Serializable] private class Choice { public Message message; }
    [Serializable] private class ChatResponse { public List<Choice> choices; }
}
