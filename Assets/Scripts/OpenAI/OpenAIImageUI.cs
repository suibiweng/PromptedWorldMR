using System;
using UnityEngine;
using UnityEngine.UI;   // RawImage & Button
using TMPro;            // TextMeshPro

public class OpenAIImageUI : MonoBehaviour
{
    [Header("UI References (TextMeshPro)")]
    public TMP_InputField promptField;
    public TMP_Dropdown sizeDropdown;     // Valid: 1024x1024, 1024x1536, 1536x1024, auto
    public TMP_Dropdown qualityDropdown;  // low/medium/high
    public TMP_Text statusLabel;

    [Header("Previews")]
    public RawImage outputPreview;
    public RawImage inputImage;   // for Edit source
    public RawImage maskImage;    // optional mask

    [Header("Buttons")]
    public Button generateButton;
    public Button editButton;

    [Header("Saving")]
    public bool saveOutputPng = false;

    private void Awake()
    {
        SetupSizeDropdownIfNeeded();
        SetupQualityDropdownIfNeeded();

        if (generateButton) generateButton.onClick.AddListener(OnGenerateClicked);
        if (editButton) editButton.onClick.AddListener(OnEditClicked);
    }

    public void OnGenerateClicked()
    {
        string prompt  = promptField ? promptField.text : "a cozy reading nook, watercolor";
        string size    = GetSelectedSize();
        string quality = GetSelectedQuality();

        SetStatus($"Generating… ({size}, {quality})");

        StartCoroutine(OpenAIImageService.GenerateFromPrompt(
            prompt: prompt,
            size: size,
            quality: quality,
            onDone: tex =>
            {
                SetRawImage(outputPreview, tex);
            },
            onError: err =>
            {
                Debug.LogError("[OpenAIImageUI_TMP] Generate error: " + err);
                SetStatus("<color=#FF5555>Generate error</color>");
            },
            savePng: saveOutputPng,
            savePath: null,
            onElapsedSeconds: secs =>
            {
                SetStatus($"Generated in {secs:0.00}s ({size}, {quality})");
            }
        ));
    }

    public void OnEditClicked()
    {
        string prompt  = promptField ? promptField.text : "subtle color correction";
        string size    = GetSelectedSize();
        string quality = GetSelectedQuality();

        Texture2D sourceTex = inputImage ? GetTexture2DFromRawImage(inputImage) : null;
        if (sourceTex == null)
        {
            Debug.LogError("[OpenAIImageUI_TMP] No valid source image. Assign 'inputImage' RawImage with a Texture.");
            SetStatus("<color=#FF5555>No source image</color>");
            return;
        }

        Texture2D maskTex = maskImage ? GetTexture2DFromRawImage(maskImage) : null;

        SetStatus($"Editing… ({size}, {quality})");

        StartCoroutine(OpenAIImageService.EditImage(
            sourceTexture: sourceTex,
            maskTexture: maskTex,
            prompt: prompt,
            size: size,
            quality: quality,
            onDone: tex =>
            {
                SetRawImage(outputPreview, tex);
            },
            onError: err =>
            {
                Debug.LogError("[OpenAIImageUI_TMP] Edit error: " + err);
                SetStatus("<color=#FF5555>Edit error</color>");
            },
            savePng: saveOutputPng,
            savePath: null,
            onElapsedSeconds: secs =>
            {
                SetStatus($"Edited in {secs:0.00}s ({size}, {quality})");
            }
        ));
    }

    // ---------------------------
    // Helpers
    // ---------------------------

    private string GetSelectedSize()
    {
        if (sizeDropdown == null || sizeDropdown.options.Count == 0)
            return "1024x1024";
        var raw = sizeDropdown.options[sizeDropdown.value].text;
        var space = raw.IndexOf(' ');
        return (space > 0) ? raw.Substring(0, space) : raw;
    }

    private string GetSelectedQuality()
    {
        if (qualityDropdown == null || qualityDropdown.options.Count == 0)
            return "low";
        return qualityDropdown.options[qualityDropdown.value].text.ToLowerInvariant();
    }

    private void SetRawImage(RawImage ri, Texture tex)
    {
        if (!ri) return;
        ri.texture = tex;
    }

    private void SetStatus(string msg)
    {
        if (statusLabel) statusLabel.text = msg;
    }

    private Texture2D GetTexture2DFromRawImage(RawImage ri)
    {
        if (!ri || ri.texture == null) return null;
        var srcTex2D = ri.texture as Texture2D;
        if (srcTex2D != null)
            return CopyToReadableTexture2D(srcTex2D);
        return RenderTextureToTexture2D(ri.texture);
    }

    private Texture2D RenderTextureToTexture2D(Texture src)
    {
        int w = src.width, h = src.height;
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        var tex2D = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tex2D.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex2D.Apply();

        RenderTexture.active = prev;
        rt.Release();
        return tex2D;
    }

    private Texture2D CopyToReadableTexture2D(Texture2D src)
    {
        int w = src.width, h = src.height;
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        copy.Apply();

        RenderTexture.active = prev;
        rt.Release();
        return copy;
    }

    private void SetupSizeDropdownIfNeeded()
    {
        if (!sizeDropdown || sizeDropdown.options.Count > 0) return;
        sizeDropdown.options.Add(new TMP_Dropdown.OptionData("1024x1024"));
        sizeDropdown.options.Add(new TMP_Dropdown.OptionData("1024x1536 (portrait)"));
        sizeDropdown.options.Add(new TMP_Dropdown.OptionData("1536x1024 (landscape)"));
        sizeDropdown.options.Add(new TMP_Dropdown.OptionData("auto"));
        sizeDropdown.value = 0;
        sizeDropdown.RefreshShownValue();
    }

    private void SetupQualityDropdownIfNeeded()
    {
        if (!qualityDropdown || qualityDropdown.options.Count > 0) return;
        qualityDropdown.options.Add(new TMP_Dropdown.OptionData("low"));
        qualityDropdown.options.Add(new TMP_Dropdown.OptionData("medium"));
        qualityDropdown.options.Add(new TMP_Dropdown.OptionData("high"));
        qualityDropdown.value = 1;
        qualityDropdown.RefreshShownValue();
    }
}
