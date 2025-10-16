using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Unified OpenAI Image Service for Unity
/// - Loads API key from Resources/Secrets/openai_api_key.txt
/// - Supports prompt-based image generation and image editing
/// - Returns Texture2D or saves PNG to disk
/// </summary>
public class OpenAIImageService : MonoBehaviour
{
    [Header("API Key Settings")]
    [Tooltip("Secrets key file. e.g., 'Secrets/openai_api_key' (no extension)")]
    [SerializeField] private string apiKeyResourcePath = "Secrets/openai_api_key";
    [Tooltip("Also set Environment variable OPENAI_API_KEY in Editor/Standalone")]
    [SerializeField] private bool setEnvVarToo = false;

    private static string apiKeyCache;

    private const string ImageModel = "gpt-image-1";
    private const string UrlGenerations = "https://api.openai.com/v1/images/generations";
    private const string UrlEdits = "https://api.openai.com/v1/images/edits";

    private void Awake()
    {
        // Load the key once at startup
        apiKeyCache = LoadFromResources(apiKeyResourcePath);
        if (string.IsNullOrEmpty(apiKeyCache))
        {
            Debug.LogError($"[OpenAIImageService] Missing key at Resources/{apiKeyResourcePath}.txt");
            return;
        }

        if (setEnvVarToo)
        {
            try
            {
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", apiKeyCache);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Unable to set environment variable: " + e.Message);
            }
        }

        Debug.Log("[OpenAIImageService] API key loaded successfully.");
    }

    private static string ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(apiKeyCache))
            return apiKeyCache;

        try
        {
            var fromEnv = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(fromEnv))
                return fromEnv;
        }
        catch { }

        var ta = Resources.Load<TextAsset>("Secrets/openai_api_key");
        if (ta != null)
            return ta.text?.Trim();

        return null;
    }

    private static string LoadFromResources(string pathNoExt)
    {
        var ta = Resources.Load<TextAsset>(pathNoExt);
        if (ta == null) return null;
        return ta.text?.Trim();
    }

    // ------------------------------------------------------------
    // PUBLIC METHODS
    // ------------------------------------------------------------

    public static IEnumerator GenerateFromPrompt(
        string prompt,
        string size = "1024x1024",
        string quality = "medium",
        Action<Texture2D> onDone = null,
        Action<string> onError = null,
        bool savePng = false,
        string savePath = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            onError?.Invoke("Prompt is empty.");
            yield break;
        }

        string apiKey = ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            onError?.Invoke("Missing API key.");
            yield break;
        }

        var reqBody = new ImageGenerationRequest
        {
            model = ImageModel,
            prompt = prompt,
            size = size,
            quality = quality,
            response_format = "b64_json"
        };

        byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(reqBody));

        using (var www = new UnityWebRequest(UrlGenerations, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return www.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
#else
            if (www.isHttpError || www.isNetworkError)
#endif
            {
                onError?.Invoke($"HTTP Error {www.responseCode}: {www.error}\n{www.downloadHandler.text}");
                yield break;
            }

            var json = www.downloadHandler.text;
            ImageGenerationResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ImageGenerationResponse>(json);
            }
            catch (Exception e)
            {
                onError?.Invoke("JSON parse error: " + e.Message);
                yield break;
            }

            if (resp?.data == null || resp.data.Length == 0)
            {
                onError?.Invoke("No data in response.");
                yield break;
            }

            var tex = DecodeBase64Png(resp.data[0].b64_json);
            if (tex == null)
            {
                onError?.Invoke("Decode error.");
                yield break;
            }

            if (savePng)
                SavePng(tex, savePath ?? "openai_gen_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

            onDone?.Invoke(tex);
        }
    }

    public static IEnumerator EditImage(
        Texture2D sourceTexture,
        Texture2D maskTexture,
        string prompt,
        string size = "1024x1024",
        string quality = "medium",
        Action<Texture2D> onDone = null,
        Action<string> onError = null,
        bool savePng = false,
        string savePath = null)
    {
        if (sourceTexture == null)
        {
            onError?.Invoke("Source texture null.");
            yield break;
        }

        string apiKey = ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            onError?.Invoke("Missing API key.");
            yield break;
        }

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("model", ImageModel),
            new MultipartFormDataSection("prompt", prompt),
            new MultipartFormDataSection("size", size),
            new MultipartFormDataSection("quality", quality),
            new MultipartFormDataSection("response_format", "b64_json")
        };

        byte[] imageBytes = sourceTexture.EncodeToPNG();
        form.Add(new MultipartFormFileSection("image", imageBytes, "image.png", "image/png"));

        if (maskTexture != null)
        {
            byte[] maskBytes = maskTexture.EncodeToPNG();
            form.Add(new MultipartFormFileSection("mask", maskBytes, "mask.png", "image/png"));
        }

        using (var www = UnityWebRequest.Post(UrlEdits, form))
        {
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            yield return www.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
#else
            if (www.isHttpError || www.isNetworkError)
#endif
            {
                onError?.Invoke($"HTTP Error {www.responseCode}: {www.error}\n{www.downloadHandler.text}");
                yield break;
            }

            var json = www.downloadHandler.text;
            ImageGenerationResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<ImageGenerationResponse>(json);
            }
            catch (Exception e)
            {
                onError?.Invoke("JSON parse error: " + e.Message);
                yield break;
            }

            if (resp?.data == null || resp.data.Length == 0)
            {
                onError?.Invoke("No data in response.");
                yield break;
            }

            var tex = DecodeBase64Png(resp.data[0].b64_json);
            if (tex == null)
            {
                onError?.Invoke("Decode error.");
                yield break;
            }

            if (savePng)
                SavePng(tex, savePath ?? "openai_edit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

            onDone?.Invoke(tex);
        }
    }

    // ------------------------------------------------------------
    // INTERNAL HELPERS
    // ------------------------------------------------------------

    private static Texture2D DecodeBase64Png(string b64)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(b64);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes, false);
            return tex;
        }
        catch (Exception e)
        {
            Debug.LogError("DecodeBase64Png failed: " + e.Message);
            return null;
        }
    }

    private static void SavePng(Texture2D tex, string filename)
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("Saved image: " + path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Failed to save PNG: " + e.Message);
        }
    }

    [Serializable]
    private class ImageGenerationRequest
    {
        public string model;
        public string prompt;
        public string size;
        public string quality;
        public string response_format;
    }

    [Serializable]
    private class ImageGenerationResponse
    {
        public ImageData[] data;
    }

    [Serializable]
    private class ImageData
    {
        public string b64_json;
        public string url;
    }
}
