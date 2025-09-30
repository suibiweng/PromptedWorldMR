using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class ChatDemo : MonoBehaviour
{
    public OpenAIResponsesClient client;  // assign in Inspector
    public TMP_InputField promptInput;
    public TMP_Text outputText;
    public RawImage imagePreview; // optional


    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            OnSendPrompt();
        }
    }

    public async void OnSendPrompt()
    {
        string prompt = promptInput.text;
        string response = await client.SendTextAsync(prompt);
        outputText.text = response ?? "(no response)";
    }

    public async void OnSendPromptWithImage()
    {
        string prompt = promptInput.text;
        Texture2D tex = imagePreview.texture as Texture2D;
        if (tex == null)
        {
            outputText.text = "No Texture2D assigned.";
            return;
        }
        string response = await client.SendTextWithImageAsync(prompt, tex);
        outputText.text = response ?? "(no response)";
    }

    public async void OnSendPromptWithImageUrl(string url)
    {
        string prompt = promptInput.text;
        string response = await client.SendTextWithImageUrlAsync(prompt, url);
        outputText.text = response ?? "(no response)";
    }
}
