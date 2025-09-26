// Assets/Scripts/OpenAIResponsesClient.cs
// Requires: com.unity.nuget.newtonsoft-json (Window → Package Manager)

using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class OpenAIResponsesClient : MonoBehaviour
{
    [Header("🔐 Set at runtime or in Inspector (avoid committing keys)")]
    [SerializeField] private string openAIApiKey = "";

    [Header("🧠 Model")]
    [SerializeField] private string model = "gpt-4.1-mini"; // or "gpt-5-mini"

    private const string Endpoint = "https://api.openai.com/v1/responses";

    // --- PUBLIC API ---------------------------------------------------------

    /// <summary>
    /// Send plain text prompt -> returns output text.
    /// </summary>
    public Task<string> SendTextAsync(string prompt)
    {
        var body = new JObject
        {
            ["model"] = model,
            // You can also use "input": "..." (single-shot).
            // Here we use chat-style for future extensibility.
            ["input"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject { ["type"] = "input_text", ["text"] = prompt }
                    }
                }
            }
        };

        return PostAsync(body);
    }

    /// <summary>
    /// Send text + Texture2D image -> returns output text.
    /// Encodes image to JPEG base64 (downscale & quality to keep payload small).
    /// </summary>
    public Task<string> SendTextWithImageAsync(string prompt, Texture2D image, int maxSize = 768, int jpegQuality = 85)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        // Downscale to keep request size reasonable
        var resized = ResizeTexture(image, maxSize);
        byte[] jpg = resized.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
        string b64 = Convert.ToBase64String(jpg);
        string dataUrl = $"data:image/jpeg;base64,{b64}";

        var body = new JObject
        {
            ["model"] = model,
            ["input"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject { ["type"] = "input_text",  ["text"] = prompt },
                        new JObject { ["type"] = "input_image", ["image_data"] = dataUrl }
                    }
                }
            }
        };

        return PostAsync(body);
    }

    /// <summary>
    /// Alternative: send text + image by URL (publicly accessible URL).
    /// </summary>
    public Task<string> SendTextWithImageUrlAsync(string prompt, string imageUrl)
    {
        var body = new JObject
        {
            ["model"] = model,
            ["input"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject { ["type"] = "input_text",  ["text"] = prompt },
                        new JObject { ["type"] = "input_image", ["image_url"] = imageUrl }
                    }
                }
            }
        };

        return PostAsync(body);
    }

    // --- CORE HTTP + PARSING -----------------------------------------------

    private async Task<string> PostAsync(JObject body)
    {
        if (string.IsNullOrEmpty(openAIApiKey))
        {
            Debug.LogError("OpenAI API key is empty. Set it on the component or supply at runtime.");
            return null;
        }

        var json = body.ToString();
        using var req = new UnityWebRequest(Endpoint, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"OpenAI HTTP error {req.responseCode}: {req.error}\n{req.downloadHandler.text}");
            return null;
        }

        var responseText = req.downloadHandler.text;

        // Try parse output_text; fall back to raw JSON if schema changes.
        try
        {
            var jo = JObject.Parse(responseText);

            // Preferred: convenience field "output_text"
            var outputText = jo["output_text"]?.ToString();
            if (!string.IsNullOrEmpty(outputText))
                return outputText;

            // Fallbacks:
            // Sometimes the SDK/response uses 'output[0].content[0].text' or tool outputs. Try a best-effort scan.
            var output = jo["output"] as JArray;
            if (output != null && output.Count > 0)
            {
                var first = output[0]?["content"] as JArray;
                if (first != null)
                {
                    foreach (var c in first)
                    {
                        if ((string)c?["type"] == "output_text")
                            return c?["text"]?.ToString();
                    }
                }
            }

            // Last resort: return the whole JSON for inspection.
            Debug.LogWarning("Could not find output_text; returning raw JSON.");
            return responseText;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Response parse error: {e.Message}. Returning raw JSON.");
            return responseText;
        }
    }

    // --- UTIL: simple CPU resize (keeps aspect) ----------------------------

    private Texture2D ResizeTexture(Texture2D src, int maxSize)
    {
        int w = src.width;
        int h = src.height;
        float scale = 1f;

        if (Mathf.Max(w, h) > maxSize)
            scale = maxSize / (float)Mathf.Max(w, h);

        int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
        int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

        if (nw == w && nh == h) return src;

        // Simple bilinear resize on CPU
        var dst = new Texture2D(nw, nh, TextureFormat.RGB24, false);
        for (int y = 0; y < nh; y++)
        {
            float v = (float)y / (nh - 1);
            for (int x = 0; x < nw; x++)
            {
                float u = (float)x / (nw - 1);
                dst.SetPixel(x, y, src.GetPixelBilinear(u, v));
            }
        }
        dst.Apply(false, false);
        return dst;
        // For production, consider Graphics.Blit or a RenderTexture pipeline for speed.
    }

    // --- OPTIONAL: public setter so you can inject key at runtime -----------

    public void SetApiKey(string key) => openAIApiKey = key;
    public void SetModel(string modelName) => model = modelName;
}
